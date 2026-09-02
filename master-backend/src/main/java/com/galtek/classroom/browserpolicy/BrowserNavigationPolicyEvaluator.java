package com.galtek.classroom.browserpolicy;

import java.util.List;

public final class BrowserNavigationPolicyEvaluator {

    private final BrowserUrlNormalizer normalizer;

    public BrowserNavigationPolicyEvaluator() {
        this(new BrowserUrlNormalizer());
    }

    public BrowserNavigationPolicyEvaluator(BrowserUrlNormalizer normalizer) {
        this.normalizer = normalizer;
    }

    public BrowserNavigationDecision evaluate(
            BrowserAccessPolicy policy,
            List<BrowserUrlRule> rules,
            String url) {
        NormalizedBrowserUrl normalized = normalizer.normalize(url).orElse(null);
        if (normalized == null) {
            return BrowserNavigationDecision.block(null, null, BrowserNavigationReasonCode.INVALID_URL);
        }

        if (policy == null) {
            return BrowserNavigationDecision.allow(null, null, BrowserNavigationReasonCode.NO_POLICY);
        }

        if (policy.mode() == BrowserPolicyMode.UNRESTRICTED) {
            return BrowserNavigationDecision.allow(policy.policyId(), null, BrowserNavigationReasonCode.UNRESTRICTED);
        }

        BrowserUrlRule block = null;
        for (BrowserUrlRule rule : rules == null ? List.<BrowserUrlRule>of() : rules) {
            if (!rule.enabled() || !matches(rule, normalized)) {
                continue;
            }
            if (rule.action() == BrowserUrlRuleAction.ALLOW) {
                return BrowserNavigationDecision.allow(
                        policy.policyId(),
                        rule.ruleId(),
                        BrowserNavigationReasonCode.EXPLICIT_ALLOW);
            }
            if (block == null && rule.action() == BrowserUrlRuleAction.BLOCK) {
                block = rule;
            }
        }

        if (block != null) {
            return BrowserNavigationDecision.block(
                    policy.policyId(),
                    block.ruleId(),
                    BrowserNavigationReasonCode.EXPLICIT_BLOCK);
        }

        if (policy.mode() == BrowserPolicyMode.BLOCKLIST) {
            return BrowserNavigationDecision.allow(policy.policyId(), null, BrowserNavigationReasonCode.DEFAULT_ALLOW);
        }

        return BrowserNavigationDecision.block(policy.policyId(), null, BrowserNavigationReasonCode.DEFAULT_BLOCK);
    }

    private boolean matches(BrowserUrlRule rule, NormalizedBrowserUrl url) {
        return switch (rule.matchType()) {
            case HOST_EXACT -> url.host().equals(rule.pattern());
            case HOST_SUFFIX -> url.host().equals(rule.pattern()) || url.host().endsWith("." + rule.pattern());
            case URL_PREFIX -> url.canonicalUrl().startsWith(rule.pattern());
            case EXACT_URL -> url.canonicalUrl().equals(rule.pattern());
        };
    }
}
