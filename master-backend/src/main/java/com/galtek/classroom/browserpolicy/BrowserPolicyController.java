package com.galtek.classroom.browserpolicy;

import com.galtek.classroom.browserpolicy.BrowserPolicyDtos.ArchiveBrowserPolicyRequest;
import com.galtek.classroom.browserpolicy.BrowserPolicyDtos.ArchiveBrowserUrlRuleRequest;
import com.galtek.classroom.browserpolicy.BrowserPolicyDtos.BrowserPolicyResponse;
import com.galtek.classroom.browserpolicy.BrowserPolicyDtos.BrowserUrlRuleResponse;
import com.galtek.classroom.browserpolicy.BrowserPolicyDtos.CreateBrowserPolicyRequest;
import com.galtek.classroom.browserpolicy.BrowserPolicyDtos.CreateBrowserUrlRuleRequest;
import com.galtek.classroom.browserpolicy.BrowserPolicyDtos.EffectiveBrowserPolicyResponse;
import com.galtek.classroom.browserpolicy.BrowserPolicyDtos.UpdateBrowserPolicyRequest;
import com.galtek.classroom.browserpolicy.BrowserPolicyDtos.UpdateBrowserUrlRuleRequest;
import java.util.List;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PatchMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping("/api")
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class BrowserPolicyController {

    private final BrowserPolicyAdminService service;

    public BrowserPolicyController(BrowserPolicyAdminService service) {
        this.service = service;
    }

    @GetMapping("/classrooms/{classroomId}/browser-policies")
    public List<BrowserPolicyResponse> policies(
            @PathVariable String classroomId,
            @RequestParam(required = false) Boolean active) {
        return service.policies(classroomId, active);
    }

    @PostMapping("/classrooms/{classroomId}/browser-policies")
    public ResponseEntity<BrowserPolicyResponse> createPolicy(
            @PathVariable String classroomId,
            @RequestBody(required = false) CreateBrowserPolicyRequest request) {
        return ResponseEntity.status(HttpStatus.CREATED).body(service.createPolicy(classroomId, request));
    }

    @PatchMapping("/browser-policies/{policyId}")
    public BrowserPolicyResponse updatePolicy(
            @PathVariable String policyId,
            @RequestBody(required = false) UpdateBrowserPolicyRequest request) {
        return service.updatePolicy(policyId, request);
    }

    @PostMapping("/browser-policies/{policyId}/archive")
    public BrowserPolicyResponse archivePolicy(
            @PathVariable String policyId,
            @RequestBody(required = false) ArchiveBrowserPolicyRequest request) {
        return service.archivePolicy(policyId, request);
    }

    @GetMapping("/browser-policies/{policyId}/rules")
    public List<BrowserUrlRuleResponse> rules(@PathVariable String policyId) {
        return service.rules(policyId);
    }

    @PostMapping("/browser-policies/{policyId}/rules")
    public ResponseEntity<BrowserUrlRuleResponse> createRule(
            @PathVariable String policyId,
            @RequestBody(required = false) CreateBrowserUrlRuleRequest request) {
        return ResponseEntity.status(HttpStatus.CREATED).body(service.createRule(policyId, request));
    }

    @PatchMapping("/browser-url-rules/{ruleId}")
    public BrowserUrlRuleResponse updateRule(
            @PathVariable String ruleId,
            @RequestBody(required = false) UpdateBrowserUrlRuleRequest request) {
        return service.updateRule(ruleId, request);
    }

    @PostMapping("/browser-url-rules/{ruleId}/archive")
    public BrowserUrlRuleResponse archiveRule(
            @PathVariable String ruleId,
            @RequestBody(required = false) ArchiveBrowserUrlRuleRequest request) {
        return service.archiveRule(ruleId, request);
    }

    @GetMapping("/classrooms/{classroomId}/browser-policies/effective")
    public EffectiveBrowserPolicyResponse effectivePolicy(
            @PathVariable String classroomId,
            @RequestParam(required = false) String deviceId,
            @RequestParam(required = false) String groupId,
            @RequestParam(required = false) String accountType) {
        return service.effectivePolicy(classroomId, deviceId, groupId, accountType);
    }
}
