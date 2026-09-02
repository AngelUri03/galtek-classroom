package com.galtek.classroom.browserpolicy;

import java.util.List;

public final class BrowserDownloadPolicyPrecedenceResolver {

    public BrowserDownloadPolicyResolution resolve(
            BrowserDownloadPolicyContext context,
            List<BrowserDownloadPolicy> policies) {
        BrowserPolicyAccountScope accountScope = context.accountScope();
        BrowserDownloadPolicy policy;

        if (context.deviceId() != null) {
            if (isSpecific(accountScope)) {
                policy = find(policies, context.classroomId(), BrowserPolicyScopeType.DEVICE,
                        context.deviceId(), accountScope);
                if (policy != null) {
                    return BrowserDownloadPolicyResolution.effective(policy);
                }
            }
            policy = find(policies, context.classroomId(), BrowserPolicyScopeType.DEVICE,
                    context.deviceId(), BrowserPolicyAccountScope.ANY);
            if (policy != null) {
                return BrowserDownloadPolicyResolution.effective(policy);
            }
        }

        if (context.schoolGroupId() != null) {
            if (isSpecific(accountScope)) {
                policy = find(policies, context.classroomId(), BrowserPolicyScopeType.GROUP,
                        context.schoolGroupId(), accountScope);
                if (policy != null) {
                    return BrowserDownloadPolicyResolution.effective(policy);
                }
            }
            policy = find(policies, context.classroomId(), BrowserPolicyScopeType.GROUP,
                    context.schoolGroupId(), BrowserPolicyAccountScope.ANY);
            if (policy != null) {
                return BrowserDownloadPolicyResolution.effective(policy);
            }
        }

        if (isSpecific(accountScope)) {
            policy = find(policies, context.classroomId(), BrowserPolicyScopeType.CLASSROOM,
                    context.classroomId(), accountScope);
            if (policy != null) {
                return BrowserDownloadPolicyResolution.effective(policy);
            }
        }

        policy = find(policies, context.classroomId(), BrowserPolicyScopeType.CLASSROOM,
                context.classroomId(), BrowserPolicyAccountScope.ANY);
        if (policy != null) {
            return BrowserDownloadPolicyResolution.effective(policy);
        }

        return BrowserDownloadPolicyResolution.implicitNoSpecialRestrictions();
    }

    private boolean isSpecific(BrowserPolicyAccountScope accountScope) {
        return accountScope == BrowserPolicyAccountScope.PRIMARY
                || accountScope == BrowserPolicyAccountScope.SECONDARY;
    }

    private BrowserDownloadPolicy find(
            List<BrowserDownloadPolicy> policies,
            String classroomId,
            BrowserPolicyScopeType scopeType,
            String targetId,
            BrowserPolicyAccountScope accountScope) {
        for (BrowserDownloadPolicy policy : policies) {
            if (!policy.active()
                    || !policy.classroomId().equals(classroomId)
                    || policy.scopeType() != scopeType
                    || policy.accountScope() != accountScope) {
                continue;
            }
            if (scopeType == BrowserPolicyScopeType.CLASSROOM && policy.classroomId().equals(targetId)) {
                return policy;
            }
            if (scopeType == BrowserPolicyScopeType.GROUP && targetId.equals(policy.schoolGroupId())) {
                return policy;
            }
            if (scopeType == BrowserPolicyScopeType.DEVICE && targetId.equals(policy.deviceId())) {
                return policy;
            }
        }
        return null;
    }
}
