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

        BrowserUrlRule best = null;
        MatchSpecificity bestSpecificity = null;
        for (BrowserUrlRule rule : rules == null ? List.<BrowserUrlRule>of() : rules) {
            MatchSpecificity specificity = specificity(rule, normalized);
            if (!rule.enabled() || specificity == null) {
                continue;
            }
            if (best == null
                    || specificity.compareTo(bestSpecificity) > 0
                    || (specificity.compareTo(bestSpecificity) == 0
                    && rule.action() == BrowserUrlRuleAction.ALLOW
                    && best.action() == BrowserUrlRuleAction.BLOCK)) {
                best = rule;
                bestSpecificity = specificity;
            }
        }

        if (best != null) {
            if (best.action() == BrowserUrlRuleAction.ALLOW) {
                return BrowserNavigationDecision.allow(
                        policy.policyId(),
                        best.ruleId(),
                        BrowserNavigationReasonCode.EXPLICIT_ALLOW);
            }
            return BrowserNavigationDecision.block(
                    policy.policyId(),
                    best.ruleId(),
                    BrowserNavigationReasonCode.EXPLICIT_BLOCK);
        }

        if (policy.mode() == BrowserPolicyMode.BLOCKLIST) {
            return BrowserNavigationDecision.allow(policy.policyId(), null, BrowserNavigationReasonCode.DEFAULT_ALLOW);
        }

        return BrowserNavigationDecision.block(policy.policyId(), null, BrowserNavigationReasonCode.DEFAULT_BLOCK);
    }

    private MatchSpecificity specificity(BrowserUrlRule rule, NormalizedBrowserUrl url) {
        return switch (rule.matchType()) {
            case HOST_EXACT -> url.host().equals(rule.pattern())
                    ? new MatchSpecificity(hostScore(rule.pattern(), true), 0, 0, 0)
                    : null;
            case HOST_SUFFIX -> url.host().equals(rule.pattern()) || url.host().endsWith("." + rule.pattern())
                    ? new MatchSpecificity(hostScore(rule.pattern(), false), 0, 0, 0)
                    : null;
            case URL_PREFIX -> url.canonicalUrl().startsWith(rule.pattern())
                    ? urlSpecificity(rule.pattern())
                    : null;
            case EXACT_URL -> url.canonicalUrl().equals(rule.pattern())
                    ? urlSpecificity(rule.pattern())
                    : null;
        };
    }

    private MatchSpecificity urlSpecificity(String pattern) {
        NormalizedBrowserUrl normalizedPattern = normalizer.normalize(pattern).orElse(null);
        if (normalizedPattern == null) {
            return new MatchSpecificity(0, 0, pattern.length(), 0);
        }
        int schemePortScore = 1 + (hasExplicitPort(pattern) ? 1 : 0);
        int pathScore = normalizedPattern.path().length();
        int queryScore = normalizedPattern.query() == null ? 0 : normalizedPattern.query().length();
        return new MatchSpecificity(hostScore(normalizedPattern.host(), true), schemePortScore, pathScore, queryScore);
    }

    private int hostScore(String host, boolean exact) {
        int labels = host.isBlank() ? 0 : host.split("\\.").length;
        return labels * 2 + (exact ? 1 : 0);
    }

    private boolean hasExplicitPort(String pattern) {
        try {
            return new java.net.URI(pattern).getPort() >= 0;
        } catch (java.net.URISyntaxException exception) {
            return false;
        }
    }

    private record MatchSpecificity(
            int host,
            int schemePort,
            int path,
            int query) implements Comparable<MatchSpecificity> {

        @Override
        public int compareTo(MatchSpecificity other) {
            int hostComparison = Integer.compare(host, other.host);
            if (hostComparison != 0) {
                return hostComparison;
            }
            int schemePortComparison = Integer.compare(schemePort, other.schemePort);
            if (schemePortComparison != 0) {
                return schemePortComparison;
            }
            int pathComparison = Integer.compare(path, other.path);
            if (pathComparison != 0) {
                return pathComparison;
            }
            return Integer.compare(query, other.query);
        }
    }
}
