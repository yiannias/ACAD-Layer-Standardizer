pub mod categorization;
pub mod config;
pub mod levenshtein;
pub mod matching;
pub mod memory;

pub use categorization::{
    LayerCategorizationResult, LayerCategorizer, LayerCategoryDefinition, LayerDictionaryDefinition,
};
pub use levenshtein::levenshtein_distance;
pub use matching::{HeuristicMatcher, MatchResult, MatchSource, MatchingEngine, MemoryMatcher};
pub use config::{config_dir, ConfigError, PluginConfig};
pub use memory::{ImportReport, MemoryError, MemoryStore, TranslationMemory};
