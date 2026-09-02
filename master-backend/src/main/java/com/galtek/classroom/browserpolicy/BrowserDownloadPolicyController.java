package com.galtek.classroom.browserpolicy;

import com.galtek.classroom.browserpolicy.BrowserDownloadPolicyDtos.ArchiveBrowserDownloadPolicyRequest;
import com.galtek.classroom.browserpolicy.BrowserDownloadPolicyDtos.BrowserDownloadPolicyResponse;
import com.galtek.classroom.browserpolicy.BrowserDownloadPolicyDtos.CreateBrowserDownloadPolicyRequest;
import com.galtek.classroom.browserpolicy.BrowserDownloadPolicyDtos.EffectiveBrowserDownloadPolicyResponse;
import com.galtek.classroom.browserpolicy.BrowserDownloadPolicyDtos.UpdateBrowserDownloadPolicyRequest;
import com.galtek.classroom.operations.OperationDtos.OperationBatchResponse;
import java.util.List;
import java.util.Map;
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
public class BrowserDownloadPolicyController {

    private final BrowserDownloadPolicyAdminService service;
    private final BrowserPolicyDispatchService dispatchService;

    public BrowserDownloadPolicyController(
            BrowserDownloadPolicyAdminService service,
            BrowserPolicyDispatchService dispatchService) {
        this.service = service;
        this.dispatchService = dispatchService;
    }

    @GetMapping("/classrooms/{classroomId}/browser-download-policies")
    public List<BrowserDownloadPolicyResponse> policies(
            @PathVariable String classroomId,
            @RequestParam(required = false) Boolean active) {
        return service.policies(classroomId, active);
    }

    @PostMapping("/classrooms/{classroomId}/browser-download-policies")
    public ResponseEntity<BrowserDownloadPolicyResponse> createPolicy(
            @PathVariable String classroomId,
            @RequestBody(required = false) CreateBrowserDownloadPolicyRequest request) {
        return ResponseEntity.status(HttpStatus.CREATED).body(service.createPolicy(classroomId, request));
    }

    @PatchMapping("/browser-download-policies/{policyId}")
    public BrowserDownloadPolicyResponse updatePolicy(
            @PathVariable String policyId,
            @RequestBody(required = false) UpdateBrowserDownloadPolicyRequest request) {
        return service.updatePolicy(policyId, request);
    }

    @PostMapping("/browser-download-policies/{policyId}/archive")
    public BrowserDownloadPolicyResponse archivePolicy(
            @PathVariable String policyId,
            @RequestBody(required = false) ArchiveBrowserDownloadPolicyRequest request) {
        return service.archivePolicy(policyId, request);
    }

    @GetMapping("/classrooms/{classroomId}/browser-download-policies/effective")
    public EffectiveBrowserDownloadPolicyResponse effectivePolicy(
            @PathVariable String classroomId,
            @RequestParam(required = false) String deviceId,
            @RequestParam(required = false) String groupId,
            @RequestParam(required = false) String accountType) {
        return service.effectivePolicy(classroomId, deviceId, groupId, accountType);
    }

    @PostMapping("/classrooms/{classroomId}/browser-download-policies/apply")
    public OperationBatchResponse applyPolicy(
            @PathVariable String classroomId,
            @RequestBody(required = false) Map<String, Object> request) {
        return dispatchService.applyDownload(classroomId, request);
    }
}
