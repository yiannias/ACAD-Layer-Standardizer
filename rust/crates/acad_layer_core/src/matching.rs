use std::collections::HashSet;
use serde::{Deserialize, Serialize};
use crate::levenshtein::levenshtein_distance;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum MatchSource {
    Memory,
    Heuristic,
    Unmatched,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MatchResult {
    pub source_layer: String,
    pub target_layer: Option<String>,
    pub confidence: f64,
    pub source: MatchSource,
}

pub struct HeuristicMatcher {
    standard_layer_names: Vec<String>,
    min_confidence: f64,
}

impl HeuristicMatcher {
    pub fn new(standard_layer_names: Vec<String>, min_confidence: f64) -> Self {
        Self {
            standard_layer_names,
            min_confidence,
        }
    }

    pub fn try_match(&self, layer_name: &str) -> Option<MatchResult> {
        let mut best: Option<MatchResult> = None;

        for standard in &self.standard_layer_names {
            let confidence = Self::calculate_similarity(layer_name, standard);
            if confidence >= self.min_confidence {
                let is_better = match &best {
                    None => true,
                    Some(b) => confidence > b.confidence,
                };

                if is_better {
                    best = Some(MatchResult {
                        source_layer: layer_name.to_string(),
                        target_layer: Some(standard.clone()),
                        confidence,
                        source: MatchSource::Heuristic,
                    });
                }
            }
        }

        best
    }

    pub fn calculate_similarity(a: &str, b: &str) -> f64 {
        if a.trim().is_empty() || b.trim().is_empty() {
            return 0.0;
        }

        let a_norm = a.trim().to_ascii_uppercase();
        let b_norm = b.trim().to_ascii_uppercase();

        let a_stripped = Self::strip_bound_prefix(&a_norm);
        let b_stripped = Self::strip_bound_prefix(&b_norm);

        if a_stripped == b_stripped {
            return 1.0;
        }

        let tokens_a = Self::tokenize(a_stripped);
        let tokens_b = Self::tokenize(b_stripped);

        if tokens_a.len() <= 1 && tokens_b.len() <= 1 {
            return Self::whole_string_similarity(a_stripped, b_stripped);
        }

        Self::token_similarity(&tokens_a, &tokens_b)
    }

    fn strip_bound_prefix(s: &str) -> &str {
        match s.rfind('$') {
            Some(idx) => &s[idx + 1..],
            None => s,
        }
    }

    fn tokenize(s: &str) -> Vec<&str> {
        s.split(|c: char| c == '-' || c == '_' || c == ' ')
            .filter(|part| !part.is_empty())
            .collect()
    }

    fn whole_string_similarity(a: &str, b: &str) -> f64 {
        if a.contains(b) || b.contains(a) {
            let (shorter_len, longer_len) = if a.len() <= b.len() {
                (a.len(), b.len())
            } else {
                (b.len(), a.len())
            };

            return 0.6 + 0.3 * (shorter_len as f64 / longer_len as f64);
        }

        let distance = levenshtein_distance(a, b);
        let max_len = a.len().max(b.len());
        1.0 - (distance as f64 / max_len as f64)
    }

    fn token_similarity(tokens_a: &[&str], tokens_b: &[&str]) -> f64 {
        let discipline_a = if !tokens_a.is_empty() && tokens_a[0].len() == 1 {
            Some(tokens_a[0])
        } else {
            None
        };

        let discipline_b = if !tokens_b.is_empty() && tokens_b[0].len() == 1 {
            Some(tokens_b[0])
        } else {
            None
        };

        let rest_a = if discipline_a.is_some() {
            &tokens_a[1..]
        } else {
            tokens_a
        };

        let rest_b = if discipline_b.is_some() {
            &tokens_b[1..]
        } else {
            tokens_b
        };

        if rest_a.is_empty() || rest_b.is_empty() {
            return Self::whole_string_similarity(&tokens_a.join("-"), &tokens_b.join("-"));
        }

        let (shorter, longer) = if rest_a.len() <= rest_b.len() {
            (rest_a, rest_b)
        } else {
            (rest_b, rest_a)
        };

        let mut total_score = 0.0;
        let mut used_indices = HashSet::new();

        for token in shorter {
            let mut best = 0.0;
            let mut best_index = None;

            for (i, longer_token) in longer.iter().enumerate() {
                if used_indices.contains(&i) {
                    continue;
                }

                let score = if *token == *longer_token {
                    1.0
                } else {
                    Self::token_fuzzy_score(token, longer_token)
                };

                if score > best {
                    best = score;
                    best_index = Some(i);
                }
            }

            if let Some(idx) = best_index {
                used_indices.insert(idx);
            }
            total_score += best;
        }

        let coverage = total_score / shorter.len() as f64;
        let extra_count = longer.len() - used_indices.len();
        let extra_penalty = 1.0 - (0.3f64).min(extra_count as f64 * 0.08);

        let mut result = coverage * extra_penalty;

        if let (Some(da), Some(db)) = (discipline_a, discipline_b) {
            if !da.eq_ignore_ascii_case(db) {
                result *= 0.3;
            }
        }

        result.clamp(0.0, 0.99)
    }

    const TOKEN_FUZZY_THRESHOLD: f64 = 0.75;

    fn token_fuzzy_score(x: &str, y: &str) -> f64 {
        if x.is_empty() || y.is_empty() {
            return 0.0;
        }

        let distance = levenshtein_distance(x, y);
        let max_len = x.len().max(y.len());
        let similarity = 1.0 - (distance as f64 / max_len as f64);

        if similarity >= Self::TOKEN_FUZZY_THRESHOLD {
            similarity
        } else {
            0.0
        }
    }
}

pub struct MemoryMatcher<'a> {
    mappings: &'a std::collections::HashMap<String, String>,
}

impl<'a> MemoryMatcher<'a> {
    pub fn new(mappings: &'a std::collections::HashMap<String, String>) -> Self {
        Self { mappings }
    }

    pub fn try_match(&self, layer_name: &str) -> Option<MatchResult> {
        self.mappings.get(layer_name).map(|target| MatchResult {
            source_layer: layer_name.to_string(),
            target_layer: Some(target.clone()),
            confidence: 1.0,
            source: MatchSource::Memory,
        })
    }
}

pub struct MatchingEngine<'a> {
    memory_matcher: Option<MemoryMatcher<'a>>,
    heuristic_matcher: Option<HeuristicMatcher>,
}

impl<'a> MatchingEngine<'a> {
    pub fn new(
        memory_matcher: Option<MemoryMatcher<'a>>,
        heuristic_matcher: Option<HeuristicMatcher>,
    ) -> Self {
        Self {
            memory_matcher,
            heuristic_matcher,
        }
    }

    pub fn classify(&self, layer_name: &str) -> MatchResult {
        if let Some(mm) = &self.memory_matcher {
            if let Some(res) = mm.try_match(layer_name) {
                return res;
            }
        }

        if let Some(hm) = &self.heuristic_matcher {
            if let Some(res) = hm.try_match(layer_name) {
                return res;
            }
        }

        MatchResult {
            source_layer: layer_name.to_string(),
            target_layer: None,
            confidence: 0.0,
            source: MatchSource::Unmatched,
        }
    }

    pub fn classify_all(&self, layer_names: &[String]) -> Vec<MatchResult> {
        layer_names.iter().map(|n| self.classify(n)).collect()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn exact_match_returns_1_0() {
        let sim = HeuristicMatcher::calculate_similarity("L-WALL", "L-WALL");
        assert_eq!(sim, 1.0);
    }

    #[test]
    fn identical_after_case_normalization_returns_1_0() {
        let sim = HeuristicMatcher::calculate_similarity("l-wall", "L-WALL");
        assert_eq!(sim, 1.0);
    }

    #[test]
    fn missing_discipline_code_still_matches_high() {
        let sim = HeuristicMatcher::calculate_similarity("WALL", "L-WALL");
        assert!(sim >= 0.9, "expected >= 0.9, got {}", sim);
    }

    #[test]
    fn levenshtein_similar_layer_names() {
        let sim = HeuristicMatcher::calculate_similarity("L-WLL", "L-WALL");
        assert!((0.6..=0.9).contains(&sim), "got {}", sim);
    }

    #[test]
    fn completely_different_returns_low() {
        let sim = HeuristicMatcher::calculate_similarity("ABC", "XYZ");
        assert!(sim < 0.3, "got {}", sim);
    }

    #[test]
    fn empty_string_returns_zero() {
        let sim = HeuristicMatcher::calculate_similarity("", "L-WALL");
        assert_eq!(sim, 0.0);
    }

    #[test]
    fn extra_qualifier_segment_still_matches_high() {
        let sim = HeuristicMatcher::calculate_similarity("A-DOOR", "A-DOOR-FULL");
        assert!(sim >= 0.85, "got {}", sim);
    }

    #[test]
    fn different_discipline_sharing_a_word_scores_low() {
        let sim = HeuristicMatcher::calculate_similarity("S-WALL", "A-WALL");
        assert!(sim < 0.5, "got {}", sim);
    }

    #[test]
    fn word_composed_entirely_of_discipline_letters_matches_as_a_subset() {
        let sim = HeuristicMatcher::calculate_similarity("AISLE", "AISLE-WIDTH");
        assert!(sim >= 0.85, "expected AISLE vs AISLE-WIDTH to score high, got {}", sim);
    }
}
