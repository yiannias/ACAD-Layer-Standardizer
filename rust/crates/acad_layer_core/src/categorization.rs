use serde::{Deserialize, Serialize};
use std::collections::{HashMap, HashSet};

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct LayerCategoryDefinition {
    pub name: String,
    pub description: Option<String>,
    #[serde(default)]
    pub tokens: Vec<String>,
    #[serde(default)]
    pub match_anywhere: bool,
    #[serde(default)]
    pub exclusive: bool,
    pub fallback_group: Option<String>,
    #[serde(default = "default_sort_group")]
    pub sort_group: String,
    #[serde(default)]
    pub always_show: bool,
}

fn default_sort_group() -> String {
    "Specific".to_string()
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct LayerDictionaryDefinition {
    #[serde(default = "default_schema_version")]
    pub schema_version: i32,
    pub description: Option<String>,
    #[serde(default = "default_delimiter")]
    pub delimiter: String,
    #[serde(default = "default_fields_scanned")]
    pub fields_scanned: usize,
    #[serde(default = "default_fold_threshold")]
    pub fold_threshold: usize,
    #[serde(default)]
    pub excluded_prefixes: Vec<String>,
    #[serde(default)]
    pub excluded_layers: Vec<String>,
    #[serde(default)]
    pub categories: Vec<LayerCategoryDefinition>,
}

fn default_schema_version() -> i32 {
    1
}

fn default_delimiter() -> String {
    "-".to_string()
}

fn default_fields_scanned() -> usize {
    3
}

fn default_fold_threshold() -> usize {
    5
}

impl Default for LayerDictionaryDefinition {
    fn default() -> Self {
        Self {
            schema_version: 1,
            description: None,
            delimiter: "-".to_string(),
            fields_scanned: 3,
            fold_threshold: 5,
            excluded_prefixes: Vec::new(),
            excluded_layers: Vec::new(),
            categories: Vec::new(),
        }
    }
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct LayerCategorizationResult {
    /// Layer name -> Set of assigned tags
    pub layer_tags: HashMap<String, HashSet<String>>,
    /// Layers that are hidden/excluded
    pub always_hidden: HashSet<String>,
    /// Visible category tags in display order
    pub visible_categories: Vec<String>,
    /// Tag name -> "Discipline" | "General" | "Specific"
    pub sort_group_by_tag: HashMap<String, String>,
}

pub struct LayerCategorizer;

impl LayerCategorizer {
    pub const MISC_CATEGORY: &'static str = "Misc";

    pub fn classify<I, S>(
        layer_names: I,
        dict: &LayerDictionaryDefinition,
    ) -> LayerCategorizationResult
    where
        I: IntoIterator<Item = S>,
        S: AsRef<str>,
    {
        let mut result = LayerCategorizationResult::default();

        let excluded_layers: HashSet<String> = dict
            .excluded_layers
            .iter()
            .map(|s| s.to_ascii_uppercase())
            .collect();

        let mut remaining = Vec::new();

        for item in layer_names {
            let name = item.as_ref();
            let upper = name.to_ascii_uppercase();

            let is_excluded = excluded_layers.contains(&upper)
                || dict
                    .excluded_prefixes
                    .iter()
                    .any(|prefix| upper.starts_with(&prefix.to_ascii_uppercase()));

            if is_excluded {
                result.always_hidden.insert(name.to_string());
            } else {
                remaining.push(name.to_string());
                result.layer_tags.insert(name.to_string(), HashSet::new());
            }
        }

        let fields_by_layer: HashMap<String, Vec<String>> = remaining
            .iter()
            .map(|n| {
                let parts: Vec<String> = if dict.delimiter.is_empty() {
                    vec![n.clone()]
                } else {
                    n.split(&dict.delimiter).map(|s| s.to_string()).collect()
                };
                (n.clone(), parts)
            })
            .collect();

        // Pass 1: Exclusive categories claim layers outright
        let mut claimed_exclusive: HashSet<String> = HashSet::new();
        for cat in dict.categories.iter().filter(|c| c.exclusive) {
            for name in &remaining {
                if claimed_exclusive.contains(name) {
                    continue;
                }
                if let Some(fields) = fields_by_layer.get(name) {
                    if Self::matches_category(fields, cat, dict.fields_scanned) {
                        let tags = result.layer_tags.get_mut(name).unwrap();
                        tags.clear();
                        tags.insert(cat.name.clone());
                        claimed_exclusive.insert(name.clone());
                    }
                }
            }
        }

        // Pass 2: Non-exclusive categories, additive
        let non_exclusive_cats: Vec<&LayerCategoryDefinition> =
            dict.categories.iter().filter(|c| !c.exclusive).collect();

        for cat in &non_exclusive_cats {
            let is_discipline = cat.sort_group.eq_ignore_ascii_case("Discipline");
            for name in &remaining {
                if claimed_exclusive.contains(name) && !is_discipline {
                    continue;
                }
                if let Some(fields) = fields_by_layer.get(name) {
                    if Self::matches_category(fields, cat, dict.fields_scanned) {
                        result
                            .layer_tags
                            .get_mut(name)
                            .unwrap()
                            .insert(cat.name.clone());
                    }
                }
            }
        }

        // Pass 3: Fold categories with count < fold_threshold
        for cat in &non_exclusive_cats {
            if cat.always_show {
                continue;
            }

            let members: Vec<String> = remaining
                .iter()
                .filter(|n| {
                    result
                        .layer_tags
                        .get(*n)
                        .map_or(false, |tags| tags.contains(&cat.name))
                })
                .cloned()
                .collect();

            if members.len() >= dict.fold_threshold {
                continue;
            }

            for name in members {
                let tags = result.layer_tags.get_mut(&name).unwrap();
                tags.remove(&cat.name);
                if let Some(fb) = &cat.fallback_group {
                    if !fb.is_empty() {
                        tags.insert(fb.clone());
                    }
                }
            }
        }

        // Pass 4: Fallback to Misc for completely untagged layers
        for name in &remaining {
            let tags = result.layer_tags.get_mut(name).unwrap();
            if tags.is_empty() {
                tags.insert(Self::MISC_CATEGORY.to_string());
            }
        }

        // Calculate visible categories and sort groups
        let mut sort_group_by_category: HashMap<String, String> = HashMap::new();
        for cat in &dict.categories {
            sort_group_by_category.insert(cat.name.clone(), cat.sort_group.clone());
        }
        for cat in &dict.categories {
            if let Some(fb) = &cat.fallback_group {
                if !fb.is_empty() && !sort_group_by_category.contains_key(fb) {
                    sort_group_by_category.insert(fb.clone(), cat.sort_group.clone());
                }
            }
        }
        sort_group_by_category.insert(Self::MISC_CATEGORY.to_string(), "Specific".to_string());

        let group_of = |tag: &str| -> String {
            sort_group_by_category
                .get(tag)
                .cloned()
                .unwrap_or_else(|| "Specific".to_string())
        };

        let group_rank = |group: &str| -> i32 {
            if group.eq_ignore_ascii_case("Discipline") {
                0
            } else if group.eq_ignore_ascii_case("General") {
                1
            } else {
                2
            }
        };

        let mut all_assigned_tags: HashSet<String> = HashSet::new();
        for tags in result.layer_tags.values() {
            for tag in tags {
                all_assigned_tags.insert(tag.clone());
            }
        }

        let mut sorted_tags: Vec<String> = all_assigned_tags.into_iter().collect();
        sorted_tags.sort_by(|a, b| {
            let rank_a = group_rank(&group_of(a));
            let rank_b = group_rank(&group_of(b));

            rank_a
                .cmp(&rank_b)
                .then_with(|| a.to_ascii_uppercase().cmp(&b.to_ascii_uppercase()))
        });

        for tag in &sorted_tags {
            result.sort_group_by_tag.insert(tag.clone(), group_of(tag));
        }

        result.visible_categories = sorted_tags;

        result
    }

    fn matches_category(
        fields: &[String],
        cat: &LayerCategoryDefinition,
        fields_scanned: usize,
    ) -> bool {
        let window: &[String] = if cat.match_anywhere {
            fields
        } else {
            let take_len = fields.len().min(fields_scanned);
            &fields[..take_len]
        };

        for field in window {
            for token in &cat.tokens {
                if field.eq_ignore_ascii_case(token) {
                    return true;
                }
            }
        }

        false
    }
}
