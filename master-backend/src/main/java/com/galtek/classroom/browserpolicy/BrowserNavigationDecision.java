package com.galtek.classroom.browserpolicy;

public record BrowserNavigationDecision(
        BrowserNavigationOutcome outcome,
        String policyId,
        String matchedRuleId,
        BrowserNavigationReasonCode reasonCode) {

    public static BrowserNavigationDecision allow(
            String policyId,
            String matchedRuleId,
            BrowserNavigationReasonCode reasonCode) {
        return new BrowserNavigationDecision(BrowserNavigationOutcome.ALLOW, policyId, matchedRuleId, reasonCode);
    }

    public static BrowserNavigationDecision block(
            String policyId,
            String matchedRuleId,
            BrowserNavigationReasonCode reasonCode) {
        return new BrowserNavigationDecision(BrowserNavigationOutcome.BLOCK, policyId, matchedRuleId, reasonCode);
    }
}
