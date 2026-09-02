package com.galtek.classroom.browserpolicy;

import java.util.List;

public final class BrowserPolicyPrecedenceResolver {

    public BrowserPolicyResolution resolve(BrowserPolicyContext context, List<BrowserAccessPolicy> policies) {
        BrowserPolicyAccountScope accountScope = context.accountScope();
        BrowserAccessPolicy policy;

        if (context.deviceId() != null) {
            if (isSpecific(accountScope)) {
                policy = find(policies, context.classroomId(), BrowserPolicyScopeType.DEVICE, context.deviceId(), accountScope);
                if (policy != null) {
                    return BrowserPolicyResolution.effective(policy);
                }
            }
            policy = find(policies, context.classroomId(), BrowserPolicyScopeType.DEVICE, context.deviceId(),
                    BrowserPolicyAccountScope.ANY);
            if (policy != null) {
                return BrowserPolicyResolution.effective(policy);
            }
        }

        if (context.schoolGroupId() != null) {
            if (isSpecific(accountScope)) {
                policy = find(policies, context.classroomId(), BrowserPolicyScopeType.GROUP,
                        context.schoolGroupId(), accountScope);
                if (policy != null) {
                    return BrowserPolicyResolution.effective(policy);
                }
            }
            policy = find(policies, context.classroomId(), BrowserPolicyScopeType.GROUP, context.schoolGroupId(),
                    BrowserPolicyAccountScope.ANY);
            if (policy != null) {
                return BrowserPolicyResolution.effective(policy);
            }
        }

        if (isSpecific(accountScope)) {
            policy = find(policies, context.classroomId(), BrowserPolicyScopeType.CLASSROOM,
                    context.classroomId(), accountScope);
            if (policy != null) {
                return BrowserPolicyResolution.effective(policy);
            }
        }

        policy = find(policies, context.classroomId(), BrowserPolicyScopeType.CLASSROOM, context.classroomId(),
                BrowserPolicyAccountScope.ANY);
        if (policy != null) {
            return BrowserPolicyResolution.effective(policy);
        }

        return BrowserPolicyResolution.implicitUnrestricted();
    }

    private boolean isSpecific(BrowserPolicyAccountScope accountScope) {
        return accountScope == BrowserPolicyAccountScope.PRIMARY
                || accountScope == BrowserPolicyAccountScope.SECONDARY;
    }

    private BrowserAccessPolicy find(
            List<BrowserAccessPolicy> policies,
            String classroomId,
            BrowserPolicyScopeType scopeType,
            String targetId,
            BrowserPolicyAccountScope accountScope) {
        for (BrowserAccessPolicy policy : policies) {
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
