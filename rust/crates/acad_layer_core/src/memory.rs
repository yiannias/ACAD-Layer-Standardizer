use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::fs;
use std::path::{Path, PathBuf};

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

    pub fn save(&self, memory: &TranslationMemory) -> Result<(), std::io::Error> {
        if let Some(parent) = self.file_path.parent() {
            if !parent.as_os_str().is_empty() {
                fs::create_dir_all(parent)?;
            }
        }

        let json = serde_json::to_string_pretty(memory)
            .map_err(|e| std::io::Error::new(std::io::ErrorKind::Other, e))?;
        fs::write(&self.file_path, json)
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
}
