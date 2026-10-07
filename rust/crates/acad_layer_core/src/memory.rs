use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::fs;
use std::fmt;
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TranslationMemory {
    #[serde(default = "default_schema_version")]
    pub schema_version: String,
    pub last_modified: Option<String>,
    pub user_identity: Option<String>,
    #[serde(default)]
    pub mappings: HashMap<String, String>,
}

fn default_schema_version() -> String {
    "1.0".to_string()
}

impl Default for TranslationMemory {
    fn default() -> Self {
        Self {
            schema_version: "1.0".to_string(),
            last_modified: None,
            user_identity: None,
            mappings: HashMap::new(),
        }
    }
}

impl TranslationMemory {
    /// Case-insensitive lookup, like the C# store's `OrdinalIgnoreCase` dictionary.
    pub fn lookup(&self, source: &str) -> Option<&str> {
        if let Some(target) = self.mappings.get(source) {
            return Some(target.as_str());
        }
        let wanted = source.to_lowercase();
        self.mappings
            .iter()
            .find(|(key, _)| key.to_lowercase() == wanted)
            .map(|(_, target)| target.as_str())
    }
}

impl TranslationMemory {
    /// Sets a mapping, keeping an existing key that differs only by case (as the C#
    /// store's case-insensitive dictionary does) instead of adding a duplicate.
    pub fn set_mapping(&mut self, source: &str, target: &str) {
        let wanted = source.to_lowercase();
        let existing = self
            .mappings
            .keys()
            .find(|key| key.to_lowercase() == wanted)
            .cloned();
        self.mappings
            .insert(existing.unwrap_or_else(|| source.to_string()), target.to_string());
    }

    pub fn remove_mapping(&mut self, source: &str) {
        let wanted = source.to_lowercase();
        self.mappings.retain(|key, _| key.to_lowercase() != wanted);
    }
}

#[derive(Debug, Clone, PartialEq)]
pub enum MemoryError {
    Io(String),
    Corrupt(String),
}

impl fmt::Display for MemoryError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            MemoryError::Io(message) => write!(f, "could not read the translation memory: {message}"),
            MemoryError::Corrupt(message) => {
                write!(f, "the translation memory file is not valid: {message}")
            }
        }
    }
}

impl std::error::Error for MemoryError {}

/// UTC timestamp like `2026-10-06T19:51:16.876Z`, parseable by the C# `DateTime` field.
fn utc_now_iso() -> String {
    let now = SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default();
    let secs = now.as_secs() as i64;
    let (days, rem) = (secs.div_euclid(86_400), secs.rem_euclid(86_400));
    // Civil-from-days (Howard Hinnant's algorithm).
    let z = days + 719_468;
    let era = z.div_euclid(146_097);
    let doe = z - era * 146_097;
    let yoe = (doe - doe / 1_460 + doe / 36_524 - doe / 146_096) / 365;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let day = doy - (153 * mp + 2) / 5 + 1;
    let month = if mp < 10 { mp + 3 } else { mp - 9 };
    let year = yoe + era * 400 + i64::from(month <= 2);
    format!(
        "{year:04}-{month:02}-{day:02}T{:02}:{:02}:{:02}.{:03}Z",
        rem / 3_600,
        rem % 3_600 / 60,
        rem % 60,
        now.subsec_millis()
    )
}

/// How many `.bak-` copies of the memory file are kept beside it.
const MAX_BACKUPS: usize = 10;

/// `2026-10-06T19:51:16.876Z` -> `20261006-195116-876`, the C# store's backup suffix
/// (`yyyyMMdd-HHmmss-fff`).
fn backup_stamp(iso: &str) -> String {
    let digits: String = iso.chars().filter(char::is_ascii_digit).collect();
    if digits.len() == 17 {
        format!("{}-{}-{}", &digits[..8], &digits[8..14], &digits[14..])
    } else {
        digits
    }
}

/// Outcome of importing another memory file: how many mappings it held and how many
/// were new to the current memory.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ImportReport {
    pub imported: usize,
    pub added: usize,
}

pub struct MemoryStore {
    file_path: PathBuf,
}

impl MemoryStore {
    pub fn new<P: AsRef<Path>>(file_path: P) -> Self {
        Self {
            file_path: file_path.as_ref().to_path_buf(),
        }
    }

    pub fn file_path(&self) -> &Path {
        &self.file_path
    }

    pub fn load(&self) -> TranslationMemory {
        if !self.file_path.exists() {
            return TranslationMemory::default();
        }

        match fs::read_to_string(&self.file_path) {
            Ok(content) => serde_json::from_str(&content).unwrap_or_default(),
            Err(_) => TranslationMemory::default(),
        }
    }

    /// Like `load`, but a corrupt or unreadable file is an error instead of silently
    /// becoming empty memory (which a later save would then write over the real file).
    pub fn load_checked(&self) -> Result<TranslationMemory, MemoryError> {
        if !self.file_path.exists() {
            return Ok(TranslationMemory::default());
        }
        let content =
            fs::read_to_string(&self.file_path).map_err(|e| MemoryError::Io(e.to_string()))?;
        serde_json::from_str(&content).map_err(|e| MemoryError::Corrupt(e.to_string()))
    }

    /// Stages the file, checks it parses with the same mapping count, keeps a
    /// timestamped `.bak-` copy of the previous file (as the C# store does), then
    /// renames the staged file into place.
    pub fn save(&self, memory: &TranslationMemory) -> Result<(), std::io::Error> {
        use std::io::{Error, ErrorKind};

        if let Some(parent) = self.file_path.parent() {
            if !parent.as_os_str().is_empty() {
                fs::create_dir_all(parent)?;
            }
        }

        let mut stamped = memory.clone();
        stamped.last_modified = Some(utc_now_iso());
        let json = serde_json::to_string_pretty(&stamped)
            .map_err(|e| Error::new(ErrorKind::Other, e))?;

        let file_name = self.file_path.file_name().unwrap_or_default().to_string_lossy().into_owned();
        let nanos = SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default().as_nanos();
        let temp = self.file_path.with_file_name(format!("{file_name}.{nanos}.tmp"));
        fs::write(&temp, &json)?;

        let result = (|| {
            let staged: TranslationMemory = serde_json::from_str(&fs::read_to_string(&temp)?)
                .map_err(|e| Error::new(ErrorKind::InvalidData, e))?;
            if staged.mappings.len() != stamped.mappings.len() {
                return Err(Error::new(
                    ErrorKind::InvalidData,
                    "the staged translation memory did not pass validation",
                ));
            }
            if self.file_path.exists() {
                let backup = self.file_path.with_file_name(format!(
                    "{file_name}.bak-{}",
                    backup_stamp(stamped.last_modified.as_deref().unwrap_or(""))
                ));
                fs::copy(&self.file_path, backup)?;
                self.prune_backups(&file_name);
            }
            fs::rename(&temp, &self.file_path)
        })();
        if result.is_err() {
            let _ = fs::remove_file(&temp);
        }
        result
    }

    /// Deletes all but the newest `MAX_BACKUPS` `.bak-` copies (their names sort by
    /// time). Best effort: a backup that cannot be removed is left alone.
    fn prune_backups(&self, file_name: &str) {
        let Some(dir) = self.file_path.parent() else {
            return;
        };
        let dir = if dir.as_os_str().is_empty() { Path::new(".") } else { dir };
        let prefix = format!("{file_name}.bak-");
        let Ok(entries) = fs::read_dir(dir) else {
            return;
        };
        let mut backups: Vec<String> = entries
            .filter_map(|entry| entry.ok())
            .map(|entry| entry.file_name().to_string_lossy().into_owned())
            .filter(|name| name.starts_with(&prefix))
            .collect();
        backups.sort();
        let excess = backups.len().saturating_sub(MAX_BACKUPS);
        for name in backups.into_iter().take(excess) {
            let _ = fs::remove_file(dir.join(name));
        }
    }

    /// Adds the mappings of another memory file that the current memory has no
    /// (case-insensitive) match for; existing entries win. A corrupt source or a
    /// corrupt current memory is an error and nothing is written. Writes (with the
    /// usual backup) only when something was added.
    pub fn import_from(&self, source: &Path) -> Result<ImportReport, MemoryError> {
        if !source.exists() {
            return Err(MemoryError::Io(format!("{} does not exist", source.display())));
        }
        let incoming = MemoryStore::new(source).load_checked()?;
        let imported = incoming.mappings.len();

        let same_file = match (fs::canonicalize(source), fs::canonicalize(&self.file_path)) {
            (Ok(a), Ok(b)) => a == b,
            _ => false,
        };
        if same_file {
            return Ok(ImportReport { imported, added: 0 });
        }

        let mut current = self.load_checked()?;
        let mut added = 0;
        for (key, target) in &incoming.mappings {
            if current.lookup(key).is_none() {
                current.set_mapping(key, target);
                added += 1;
            }
        }
        if added > 0 {
            self.save(&current).map_err(|e| MemoryError::Io(e.to_string()))?;
        }
        Ok(ImportReport { imported, added })
    }

    /// Writes a copy of the memory file to `destination` (creating parent folders).
    pub fn export_to(&self, destination: &Path) -> Result<(), MemoryError> {
        if !self.file_path.exists() {
            return Err(MemoryError::Io(
                "there is no translation memory to export yet".to_string(),
            ));
        }
        let memory = self.load_checked()?;
        MemoryStore::new(destination)
            .save(&memory)
            .map_err(|e| MemoryError::Io(e.to_string()))
    }

    pub fn merge(mut current: TranslationMemory, imported: TranslationMemory) -> TranslationMemory {
        for (k, v) in imported.mappings {
            current.mappings.entry(k).or_insert(v);
        }
        current
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_merge_preserves_existing() {
        let mut cur = TranslationMemory::default();
        cur.mappings
            .insert("A-WALL".to_string(), "ARCH-WALL".to_string());

        let mut imp = TranslationMemory::default();
        imp.mappings
            .insert("A-WALL".to_string(), "NEW-WALL".to_string());
        imp.mappings
            .insert("A-DOOR".to_string(), "ARCH-DOOR".to_string());

        let merged = MemoryStore::merge(cur, imp);
        assert_eq!(merged.mappings.get("A-WALL").unwrap(), "ARCH-WALL");
        assert_eq!(merged.mappings.get("A-DOOR").unwrap(), "ARCH-DOOR");
    }

    fn temp_dir(name: &str) -> std::path::PathBuf {
        let dir = std::env::temp_dir().join(format!("acad_layer_core_{name}_{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    fn fixture_1_2_x() -> std::path::PathBuf {
        std::path::PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("tests/fixtures/memory_1_2_x.json")
    }

    #[test]
    fn memory_lookup_is_case_insensitive() {
        let mut memory = TranslationMemory::default();
        memory.mappings.insert("A-Wall".into(), "A-WALL".into());
        assert_eq!(memory.lookup("a-wall"), Some("A-WALL"));
        assert_eq!(memory.lookup("A-Wall"), Some("A-WALL"));
        assert_eq!(memory.lookup("A-DOOR"), None);
    }

    #[test]
    fn memory_reads_1_2_x_file() {
        let memory = MemoryStore::new(fixture_1_2_x()).load_checked().unwrap();
        assert_eq!(memory.mappings.len(), 4);
        assert_eq!(memory.lookup("a-ELEV-medm"), Some("A-DT-3"));
        assert_eq!(memory.user_identity, None);
    }

    #[test]
    fn memory_load_checked_reports_corruption_and_allows_missing() {
        let dir = temp_dir("memory_checked");
        let corrupt = dir.join("corrupt.json");
        std::fs::write(&corrupt, "{ nope").unwrap();
        assert!(MemoryStore::new(&corrupt).load_checked().is_err());
        let missing = MemoryStore::new(dir.join("missing.json")).load_checked().unwrap();
        assert!(missing.mappings.is_empty());
    }

    #[test]
    fn memory_save_is_atomic_and_keeps_a_backup() {
        let dir = temp_dir("memory_save");
        let path = dir.join("standards_memory.json");
        let store = MemoryStore::new(&path);
        let mut memory = TranslationMemory::default();
        memory.mappings.insert("A".into(), "B".into());
        store.save(&memory).unwrap();
        memory.mappings.insert("C".into(), "D".into());
        store.save(&memory).unwrap();

        let names: Vec<String> = std::fs::read_dir(&dir)
            .unwrap()
            .map(|e| e.unwrap().file_name().to_string_lossy().into_owned())
            .collect();
        assert!(!names.iter().any(|n| n.ends_with(".tmp")), "{names:?}");
        assert!(names.iter().any(|n| n.contains(".bak-")), "second save keeps the prior file: {names:?}");
        assert_eq!(store.load_checked().unwrap().mappings.len(), 2);
    }

    fn backup_names(dir: &std::path::Path) -> Vec<String> {
        let mut names: Vec<String> = std::fs::read_dir(dir)
            .unwrap()
            .map(|e| e.unwrap().file_name().to_string_lossy().into_owned())
            .filter(|n| n.contains(".bak-"))
            .collect();
        names.sort();
        names
    }

    #[test]
    fn backups_are_named_like_the_csharp_store() {
        let dir = temp_dir("memory_bak_name");
        let store = MemoryStore::new(dir.join("standards_memory.json"));
        let memory = TranslationMemory::default();
        store.save(&memory).unwrap();
        store.save(&memory).unwrap();
        let names = backup_names(&dir);
        assert_eq!(names.len(), 1, "{names:?}");
        // C#: ".bak-" + yyyyMMdd-HHmmss-fff
        let stamp = names[0].strip_prefix("standards_memory.json.bak-").unwrap();
        let shape: String = stamp
            .chars()
            .map(|c| if c.is_ascii_digit() { '9' } else { c })
            .collect();
        assert_eq!(shape, "99999999-999999-999", "{stamp}");
    }

    #[test]
    fn only_the_newest_backups_are_kept() {
        let dir = temp_dir("memory_bak_prune");
        let store = MemoryStore::new(dir.join("standards_memory.json"));
        let memory = TranslationMemory::default();
        store.save(&memory).unwrap();
        for n in 0..12 {
            std::fs::write(
                dir.join(format!("standards_memory.json.bak-20200101-0000{n:02}-000")),
                "{}",
            )
            .unwrap();
        }
        store.save(&memory).unwrap();
        let names = backup_names(&dir);
        assert_eq!(names.len(), MAX_BACKUPS, "{names:?}");
        assert!(!names.iter().any(|n| n.ends_with("20200101-000000-000")), "{names:?}");
        assert!(
            !names.iter().any(|n| n.ends_with("20200101-000001-000")),
            "oldest go first: {names:?}"
        );
        assert!(names.iter().any(|n| n.ends_with("20200101-000011-000")), "{names:?}");
    }

    #[test]
    fn memory_saved_by_rust_has_the_csharp_shape() {
        let dir = temp_dir("memory_shape");
        let path = dir.join("m.json");
        let mut memory = TranslationMemory::default();
        memory.mappings.insert("A-Wall".into(), "A-WALL".into());
        MemoryStore::new(&path).save(&memory).unwrap();
        let value: serde_json::Value = serde_json::from_str(&std::fs::read_to_string(&path).unwrap()).unwrap();
        assert_eq!(value["schemaVersion"], "1.0");
        assert!(value["lastModified"].as_str().unwrap().ends_with('Z'));
        assert_eq!(value["mappings"]["A-Wall"], "A-WALL");
    }

    #[test]
    fn set_mapping_replaces_a_key_that_differs_only_by_case() {
        let mut memory = TranslationMemory::default();
        memory.mappings.insert("A-Wall".into(), "OLD".into());
        memory.set_mapping("a-wall", "NEW");
        assert_eq!(memory.mappings.len(), 1);
        assert_eq!(memory.lookup("A-WALL"), Some("NEW"));
    }

    #[test]
    fn remove_mapping_ignores_case() {
        let mut memory = TranslationMemory::default();
        memory.mappings.insert("A-Wall".into(), "X".into());
        memory.remove_mapping("a-WALL");
        assert!(memory.mappings.is_empty());
    }

    fn store_with(dir: &Path, file: &str, pairs: &[(&str, &str)]) -> MemoryStore {
        let store = MemoryStore::new(dir.join(file));
        let mut memory = TranslationMemory::default();
        for (k, v) in pairs {
            memory.mappings.insert((*k).into(), (*v).into());
        }
        store.save(&memory).unwrap();
        store
    }

    #[test]
    fn import_adds_only_new_mappings_and_existing_ones_win() {
        let dir = temp_dir("memory_import_new");
        let current = store_with(&dir, "cur.json", &[("A-WALL", "ARCH-WALL")]);
        let source = store_with(&dir, "src.json", &[("A-WALL", "OTHER"), ("A-DOOR", "ARCH-DOOR")]);
        let report = current.import_from(source.file_path()).unwrap();
        assert_eq!(report, ImportReport { imported: 2, added: 1 });
        let memory = current.load_checked().unwrap();
        assert_eq!(memory.lookup("A-WALL"), Some("ARCH-WALL"));
        assert_eq!(memory.lookup("A-DOOR"), Some("ARCH-DOOR"));
    }

    #[test]
    fn import_ignores_case_when_deciding_what_is_new() {
        let dir = temp_dir("memory_import_case");
        let current = store_with(&dir, "cur.json", &[("A-Wall", "X")]);
        let source = store_with(&dir, "src.json", &[("a-wall", "Y")]);
        let report = current.import_from(source.file_path()).unwrap();
        assert_eq!(report, ImportReport { imported: 1, added: 0 });
        let memory = current.load_checked().unwrap();
        assert_eq!(memory.mappings.len(), 1);
        assert_eq!(memory.lookup("A-WALL"), Some("X"));
    }

    #[test]
    fn importing_the_memory_file_into_itself_adds_nothing_and_writes_nothing() {
        let dir = temp_dir("memory_import_self");
        let store = store_with(&dir, "cur.json", &[("A", "B"), ("C", "D")]);
        let before = std::fs::read(store.file_path()).unwrap();
        let backups = backup_names(&dir);
        let report = store.import_from(store.file_path()).unwrap();
        assert_eq!(report, ImportReport { imported: 2, added: 0 });
        assert_eq!(std::fs::read(store.file_path()).unwrap(), before);
        assert_eq!(backup_names(&dir), backups);
    }

    #[test]
    fn a_corrupt_import_source_is_an_error_and_changes_nothing() {
        let dir = temp_dir("memory_import_corrupt_src");
        let store = store_with(&dir, "cur.json", &[("A", "B")]);
        let before = std::fs::read(store.file_path()).unwrap();
        let source = dir.join("src.json");
        std::fs::write(&source, "{ nope").unwrap();
        assert!(matches!(store.import_from(&source), Err(MemoryError::Corrupt(_))));
        assert_eq!(std::fs::read(store.file_path()).unwrap(), before);
        assert!(backup_names(&dir).is_empty());
        assert_eq!(std::fs::read_to_string(&source).unwrap(), "{ nope");
    }

    #[test]
    fn a_corrupt_current_memory_is_an_error_and_is_not_overwritten() {
        let dir = temp_dir("memory_import_corrupt_cur");
        let source = store_with(&dir, "src.json", &[("A", "B")]);
        let current = dir.join("cur.json");
        std::fs::write(&current, "{ nope").unwrap();
        let store = MemoryStore::new(&current);
        assert!(matches!(store.import_from(source.file_path()), Err(MemoryError::Corrupt(_))));
        assert_eq!(std::fs::read_to_string(&current).unwrap(), "{ nope");
        assert!(backup_names(&dir).is_empty());
    }

    #[test]
    fn import_into_a_missing_memory_file_creates_it() {
        let dir = temp_dir("memory_import_missing");
        let source = store_with(&dir, "src.json", &[("A", "B")]);
        let store = MemoryStore::new(dir.join("new.json"));
        let report = store.import_from(source.file_path()).unwrap();
        assert_eq!(report, ImportReport { imported: 1, added: 1 });
        assert_eq!(store.load_checked().unwrap().lookup("A"), Some("B"));
    }

    #[test]
    fn export_writes_a_copy_with_the_same_mappings() {
        let dir = temp_dir("memory_export");
        let store = store_with(&dir, "cur.json", &[("A", "B"), ("C", "D")]);
        let destination = dir.join("sub").join("copy.json");
        store.export_to(&destination).unwrap();
        let copy = MemoryStore::new(&destination).load_checked().unwrap();
        assert_eq!(copy.mappings, store.load_checked().unwrap().mappings);
    }

    #[test]
    fn export_without_a_memory_file_says_there_is_nothing_to_export() {
        let dir = temp_dir("memory_export_none");
        let store = MemoryStore::new(dir.join("missing.json"));
        let destination = dir.join("copy.json");
        assert_eq!(
            store.export_to(&destination),
            Err(MemoryError::Io("there is no translation memory to export yet".into()))
        );
        assert!(!destination.exists());
    }
}
