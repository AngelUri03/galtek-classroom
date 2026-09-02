package com.galtek.classroom.browserpolicy;

import static org.assertj.core.api.Assertions.assertThat;

import java.time.OffsetDateTime;
import java.util.List;
import org.junit.jupiter.api.Test;

class BrowserNavigationPolicyEvaluatorTest {

    private final BrowserNavigationPolicyEvaluator evaluator = new BrowserNavigationPolicyEvaluator();

    @Test
    void blocklistWithoutMatchAllowsByDefault() {
        BrowserNavigationDecision decision = evaluator.evaluate(
                policy(BrowserPolicyMode.BLOCKLIST),
                List.of(),
                "https://example.edu/");

        assertThat(decision.outcome()).isEqualTo(BrowserNavigationOutcome.ALLOW);
        assertThat(decision.reasonCode()).isEqualTo(BrowserNavigationReasonCode.DEFAULT_ALLOW);
    }

    @Test
    void blocklistMatchingBlockBlocks() {
        BrowserNavigationDecision decision = evaluator.evaluate(
                policy(BrowserPolicyMode.BLOCKLIST),
                List.of(rule("block-youtube", BrowserUrlRuleAction.BLOCK, BrowserUrlMatchType.HOST_SUFFIX, "youtube.com")),
                "https://music.youtube.com/watch?v=1");

        assertThat(decision.outcome()).isEqualTo(BrowserNavigationOutcome.BLOCK);
        assertThat(decision.matchedRuleId()).isEqualTo("block-youtube");
        assertThat(decision.reasonCode()).isEqualTo(BrowserNavigationReasonCode.EXPLICIT_BLOCK);
    }

    @Test
    void explicitAllowWinsOverBlockInsideSamePolicy() {
        BrowserNavigationDecision decision = evaluator.evaluate(
                policy(BrowserPolicyMode.BLOCKLIST),
                List.of(
                        rule("block-youtube", BrowserUrlRuleAction.BLOCK, BrowserUrlMatchType.HOST_SUFFIX, "youtube.com"),
                        rule("allow-video", BrowserUrlRuleAction.ALLOW, BrowserUrlMatchType.EXACT_URL,
                                "https://youtube.com/watch?v=ABC123")),
                "https://youtube.com/watch?v=ABC123");

        assertThat(decision.outcome()).isEqualTo(BrowserNavigationOutcome.ALLOW);
        assertThat(decision.matchedRuleId()).isEqualTo("allow-video");
        assertThat(decision.reasonCode()).isEqualTo(BrowserNavigationReasonCode.EXPLICIT_ALLOW);
    }

    @Test
    void allowlistWithoutMatchBlocksByDefault() {
        BrowserNavigationDecision decision = evaluator.evaluate(
                policy(BrowserPolicyMode.ALLOWLIST),
                List.of(),
                "https://example.edu/");

        assertThat(decision.outcome()).isEqualTo(BrowserNavigationOutcome.BLOCK);
        assertThat(decision.reasonCode()).isEqualTo(BrowserNavigationReasonCode.DEFAULT_BLOCK);
    }

    @Test
    void allowlistMatchingAllowAllows() {
        BrowserNavigationDecision decision = evaluator.evaluate(
                policy(BrowserPolicyMode.ALLOWLIST),
                List.of(rule("allow-school", BrowserUrlRuleAction.ALLOW, BrowserUrlMatchType.HOST_SUFFIX, "school.test")),
                "https://www.school.test/material");

        assertThat(decision.outcome()).isEqualTo(BrowserNavigationOutcome.ALLOW);
        assertThat(decision.reasonCode()).isEqualTo(BrowserNavigationReasonCode.EXPLICIT_ALLOW);
    }

    @Test
    void unrestrictedAllowsWithoutRules() {
        BrowserNavigationDecision decision = evaluator.evaluate(
                policy(BrowserPolicyMode.UNRESTRICTED),
                List.of(rule("block", BrowserUrlRuleAction.BLOCK, BrowserUrlMatchType.HOST_EXACT, "example.edu")),
                "https://example.edu/");

        assertThat(decision.outcome()).isEqualTo(BrowserNavigationOutcome.ALLOW);
        assertThat(decision.reasonCode()).isEqualTo(BrowserNavigationReasonCode.UNRESTRICTED);
    }

    @Test
    void invalidUrlBlocksEvenWhenPolicyWouldAllow() {
        BrowserNavigationDecision decision = evaluator.evaluate(
                policy(BrowserPolicyMode.ALLOWLIST),
                List.of(rule("allow-file", BrowserUrlRuleAction.ALLOW, BrowserUrlMatchType.EXACT_URL,
                        "file:///C:/Windows")),
                "file:///C:/Windows");

        assertThat(decision.outcome()).isEqualTo(BrowserNavigationOutcome.BLOCK);
        assertThat(decision.reasonCode()).isEqualTo(BrowserNavigationReasonCode.INVALID_URL);
    }

    @Test
    void hostExactDoesNotMatchSubdomain() {
        BrowserNavigationDecision decision = evaluator.evaluate(
                policy(BrowserPolicyMode.BLOCKLIST),
                List.of(rule("block", BrowserUrlRuleAction.BLOCK, BrowserUrlMatchType.HOST_EXACT, "youtube.com")),
                "https://www.youtube.com/");

        assertThat(decision.outcome()).isEqualTo(BrowserNavigationOutcome.ALLOW);
        assertThat(decision.reasonCode()).isEqualTo(BrowserNavigationReasonCode.DEFAULT_ALLOW);
    }

    @Test
    void hostSuffixMatchesSubdomainButNotLookalike() {
        BrowserUrlRule suffix = rule("block", BrowserUrlRuleAction.BLOCK, BrowserUrlMatchType.HOST_SUFFIX, "youtube.com");

        assertThat(evaluator.evaluate(policy(BrowserPolicyMode.BLOCKLIST), List.of(suffix),
                "https://music.youtube.com/").outcome()).isEqualTo(BrowserNavigationOutcome.BLOCK);
        assertThat(evaluator.evaluate(policy(BrowserPolicyMode.BLOCKLIST), List.of(suffix),
                "https://evilyoutube.com/").outcome()).isEqualTo(BrowserNavigationOutcome.ALLOW);
        assertThat(evaluator.evaluate(policy(BrowserPolicyMode.BLOCKLIST), List.of(suffix),
                "https://youtube.com.evil.test/").outcome()).isEqualTo(BrowserNavigationOutcome.ALLOW);
    }

    @Test
    void exactUrlDifferentiatesDifferentUrls() {
        BrowserUrlRule exact = rule("allow-video", BrowserUrlRuleAction.ALLOW, BrowserUrlMatchType.EXACT_URL,
                "https://www.youtube.com/watch?v=ABC123");

        assertThat(evaluator.evaluate(policy(BrowserPolicyMode.ALLOWLIST), List.of(exact),
                "https://www.youtube.com/watch?v=ABC123").outcome()).isEqualTo(BrowserNavigationOutcome.ALLOW);
        assertThat(evaluator.evaluate(policy(BrowserPolicyMode.ALLOWLIST), List.of(exact),
                "https://www.youtube.com/watch?v=XYZ999").outcome()).isEqualTo(BrowserNavigationOutcome.BLOCK);
    }

    @Test
    void urlPrefixMatchesOnlyValidCanonicalPrefix() {
        BrowserUrlNormalizer normalizer = new BrowserUrlNormalizer();
        assertThat(normalizer.normalizeUrlRulePattern(
                BrowserUrlMatchType.URL_PREFIX,
                "https://escuela.local/material/?unit=1")).isEmpty();

        BrowserUrlRule prefix = rule("allow-material", BrowserUrlRuleAction.ALLOW, BrowserUrlMatchType.URL_PREFIX,
                "https://escuela.local/material/");
        assertThat(evaluator.evaluate(policy(BrowserPolicyMode.ALLOWLIST), List.of(prefix),
                "https://escuela.local/material/lesson-1").outcome()).isEqualTo(BrowserNavigationOutcome.ALLOW);
        assertThat(evaluator.evaluate(policy(BrowserPolicyMode.ALLOWLIST), List.of(prefix),
                "https://escuela.local/other/lesson-1").outcome()).isEqualTo(BrowserNavigationOutcome.BLOCK);
    }

    @Test
    void fragmentsDoNotChangeDecisions() {
        BrowserUrlRule exact = rule("allow", BrowserUrlRuleAction.ALLOW, BrowserUrlMatchType.EXACT_URL,
                "https://example.edu/path?q=1");

        assertThat(evaluator.evaluate(policy(BrowserPolicyMode.ALLOWLIST), List.of(exact),
                "https://example.edu/path?q=1#fragment").outcome()).isEqualTo(BrowserNavigationOutcome.ALLOW);
    }

    @Test
    void defaultPortsNormalizeConsistently() {
        BrowserUrlRule exact = rule("allow", BrowserUrlRuleAction.ALLOW, BrowserUrlMatchType.EXACT_URL,
                "https://example.edu/path");

        assertThat(evaluator.evaluate(policy(BrowserPolicyMode.ALLOWLIST), List.of(exact),
                "https://example.edu:443/path").outcome()).isEqualTo(BrowserNavigationOutcome.ALLOW);
    }

    private BrowserAccessPolicy policy(BrowserPolicyMode mode) {
        OffsetDateTime now = OffsetDateTime.parse("2026-09-01T12:00:00Z");
        return new BrowserAccessPolicy(
                "policy-1",
                "classroom-1",
                "Policy",
                mode,
                BrowserPolicyScopeType.CLASSROOM,
                null,
                null,
                BrowserPolicyAccountScope.ANY,
                true,
                0,
                now,
                now);
    }

    private BrowserUrlRule rule(
            String ruleId,
            BrowserUrlRuleAction action,
            BrowserUrlMatchType matchType,
            String pattern) {
        String canonicalPattern = new BrowserUrlNormalizer().normalizeUrlRulePattern(matchType, pattern)
                .orElse(pattern);
        OffsetDateTime now = OffsetDateTime.parse("2026-09-01T12:00:00Z");
        return new BrowserUrlRule(ruleId, "policy-1", action, matchType, canonicalPattern, true, null, 0, now, now);
    }
}
