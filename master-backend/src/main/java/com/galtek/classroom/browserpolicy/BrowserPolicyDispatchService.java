package com.galtek.classroom.browserpolicy;

import com.galtek.classroom.admin.AdminDtos.AssignmentResponse;
import com.galtek.classroom.admin.AdminDtos.ClassroomResponse;
import com.galtek.classroom.admin.AdminDtos.StudentResponse;
import com.galtek.classroom.admin.MasterAdminRepository;
import com.galtek.classroom.api.ApiException;
import com.galtek.classroom.device.Device;
import com.galtek.classroom.device.DeviceCapability;
import com.galtek.classroom.device.DeviceRepository;
import com.galtek.classroom.device.DeviceStatus;
import com.galtek.classroom.localagent.MasterAuthorizationResponse;
import com.galtek.classroom.master.MasterAccessGuard;
import com.galtek.classroom.network.ClientConnectionRegistry;
import com.galtek.classroom.network.ClientConnectionSnapshot;
import com.galtek.classroom.network.DeviceNetworkBindingRepository;
import com.galtek.classroom.network.KnownMasterClient;
import com.galtek.classroom.network.MasterPairingService;
import com.galtek.classroom.network.MasterRemoteOperationGateway;
import com.galtek.classroom.network.MasterRemoteOperationGateway.DispatchHandle;
import com.galtek.classroom.network.MasterRemoteOperationGateway.RemoteOperationOutcome;
import com.galtek.classroom.network.PairingStatus;
import com.galtek.classroom.network.RegisteredNetworkDevice;
import com.galtek.classroom.network.v1.ApplyBrowserDownloadPolicyOperationParameters;
import com.galtek.classroom.network.v1.ApplyBrowserPolicyOperationParameters;
import com.galtek.classroom.network.v1.BrowserPolicyRuleParameters;
import com.galtek.classroom.operations.BatchOperation;
import com.galtek.classroom.operations.BatchOperationService;
import com.galtek.classroom.operations.BatchTargetResult;
import com.galtek.classroom.operations.ErrorCode;
import com.galtek.classroom.operations.OperationDtos.OperationBatchResponse;
import com.galtek.classroom.operations.OperationPayload;
import com.galtek.classroom.operations.OperationTarget;
import com.galtek.classroom.operations.OperationTargetType;
import com.galtek.classroom.operations.OperationType;
import com.galtek.classroom.operations.TargetExecutionStatus;
import com.galtek.classroom.persistence.MasterStorageHealth;
import com.galtek.classroom.persistence.MasterStorageState;
import com.galtek.classroom.persistence.MasterStorageStatus;
import java.time.Clock;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.ArrayList;
import java.util.Collection;
import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;
import java.util.function.Function;
import java.util.stream.Collectors;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.http.HttpStatus;
import org.springframework.stereotype.Service;

@Service
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class BrowserPolicyDispatchService {

    private static final Set<String> REQUEST_FIELDS = Set.of("targetDeviceIds");
    private static final String REQUESTED_BY = "LOCAL_MASTER";

    private final MasterAccessGuard masterAccessGuard;
    private final MasterStorageState storageState;
    private final MasterAdminRepository adminRepository;
    private final DeviceRepository deviceRepository;
    private final DeviceNetworkBindingRepository bindingRepository;
    private final MasterPairingService pairingService;
    private final ClientConnectionRegistry connectionRegistry;
    private final MasterRemoteOperationGateway remoteOperationGateway;
    private final BatchOperationService batchOperationService;
    private final BrowserPolicyRepository browserPolicyRepository;
    private final BrowserDownloadPolicyRepository browserDownloadPolicyRepository;
    private final BrowserPolicyPrecedenceResolver browserPolicyResolver;
    private final BrowserDownloadPolicyPrecedenceResolver browserDownloadPolicyResolver;
    private final Clock clock;

    public BrowserPolicyDispatchService(
            MasterAccessGuard masterAccessGuard,
            MasterStorageState storageState,
            MasterAdminRepository adminRepository,
            DeviceRepository deviceRepository,
            DeviceNetworkBindingRepository bindingRepository,
            MasterPairingService pairingService,
            ClientConnectionRegistry connectionRegistry,
            MasterRemoteOperationGateway remoteOperationGateway,
            BatchOperationService batchOperationService,
            BrowserPolicyRepository browserPolicyRepository,
            BrowserDownloadPolicyRepository browserDownloadPolicyRepository,
            Clock clock) {
        this.masterAccessGuard = masterAccessGuard;
        this.storageState = storageState;
        this.adminRepository = adminRepository;
        this.deviceRepository = deviceRepository;
        this.bindingRepository = bindingRepository;
        this.pairingService = pairingService;
        this.connectionRegistry = connectionRegistry;
        this.remoteOperationGateway = remoteOperationGateway;
        this.batchOperationService = batchOperationService;
        this.browserPolicyRepository = browserPolicyRepository;
        this.browserDownloadPolicyRepository = browserDownloadPolicyRepository;
        this.browserPolicyResolver = new BrowserPolicyPrecedenceResolver();
        this.browserDownloadPolicyResolver = new BrowserDownloadPolicyPrecedenceResolver();
        this.clock = clock;
    }

    public OperationBatchResponse applyNavigation(String classroomId, Map<String, Object> request) {
        requireAuthorizedAndStorage();
        List<String> targetDeviceIds = targetDeviceIdsFrom(request);
        ClassroomResponse classroom = classroomOr404(classroomId);
        OffsetDateTime createdAtUtc = nowUtc();
        String operationId = UUID.randomUUID().toString();

        List<TargetPlan> targetPlans = navigationTargetPlans(classroom.classroomId(), targetDeviceIds);
        return dispatch(operationId, OperationType.APPLY_BROWSER_NAVIGATION_POLICY, createdAtUtc, classroom, targetPlans);
    }

    public OperationBatchResponse applyDownload(String classroomId, Map<String, Object> request) {
        requireAuthorizedAndStorage();
        List<String> targetDeviceIds = targetDeviceIdsFrom(request);
        ClassroomResponse classroom = classroomOr404(classroomId);
        OffsetDateTime createdAtUtc = nowUtc();
        String operationId = UUID.randomUUID().toString();

        List<TargetPlan> targetPlans = downloadTargetPlans(classroom.classroomId(), targetDeviceIds);
        return dispatch(operationId, OperationType.APPLY_BROWSER_DOWNLOAD_POLICY, createdAtUtc, classroom, targetPlans);
    }

    private OperationBatchResponse dispatch(
            String operationId,
            OperationType operationType,
            OffsetDateTime createdAtUtc,
            ClassroomResponse classroom,
            List<TargetPlan> targetPlans) {
        List<BatchTargetResult> intentTargets = targetPlans.stream()
                .map(TargetPlan::intentResult)
                .toList();
        batchOperationService.create(
                classroom.classroomId(),
                BatchOperation.fromTargets(
                        operationId,
                        operationType,
                        REQUESTED_BY,
                        createdAtUtc,
                        intentTargets),
                OperationPayload.none());

        Map<String, DispatchHandle> dispatched = new LinkedHashMap<>();
        Map<String, BatchTargetResult> finalResultsByDeviceId = new LinkedHashMap<>();
        for (TargetPlan targetPlan : targetPlans) {
            if (!targetPlan.ready()) {
                finalResultsByDeviceId.put(targetPlan.deviceId(), targetPlan.finalFailure());
                continue;
            }

            var handle = switch (operationType) {
                case APPLY_BROWSER_NAVIGATION_POLICY -> remoteOperationGateway.dispatch(
                        targetPlan.snapshot(),
                        operationType,
                        operationId,
                        targetPlan.deviceId(),
                        targetPlan.navigationParameters());
                case APPLY_BROWSER_DOWNLOAD_POLICY -> remoteOperationGateway.dispatch(
                        targetPlan.snapshot(),
                        operationType,
                        operationId,
                        targetPlan.deviceId(),
                        targetPlan.downloadParameters());
                default -> java.util.Optional.<DispatchHandle>empty();
            };
            if (handle.isEmpty()) {
                finalResultsByDeviceId.put(
                        targetPlan.deviceId(),
                        failed(
                                targetPlan.target(),
                                ErrorCode.DEVICE_OFFLINE,
                                "Device is offline.",
                                1));
                continue;
            }

            dispatched.put(targetPlan.deviceId(), handle.get());
        }

        long deadlineNanos = System.nanoTime() + remoteOperationGateway.resultTimeout().toNanos();
        for (TargetPlan targetPlan : targetPlans) {
            DispatchHandle handle = dispatched.get(targetPlan.deviceId());
            if (handle == null) {
                continue;
            }

            RemoteOperationOutcome outcome = awaitOutcome(handle, deadlineNanos);
            finalResultsByDeviceId.put(
                    targetPlan.deviceId(),
                    new BatchTargetResult(
                            targetPlan.target(),
                            outcome.status(),
                            outcome.errorCode(),
                            outcome.message(),
                            1));
        }

        List<BatchTargetResult> finalResults = targetPlans.stream()
                .map(targetPlan -> finalResultsByDeviceId.get(targetPlan.deviceId()))
                .toList();
        BatchOperation completed = BatchOperation.fromTargets(
                operationId,
                operationType,
                REQUESTED_BY,
                createdAtUtc,
                finalResults);
        batchOperationService.replaceResults(completed);
        return OperationBatchResponse.from(completed);
    }

    private List<TargetPlan> navigationTargetPlans(String classroomId, List<String> targetDeviceIds) {
        TargetContext context = targetContext(classroomId, targetDeviceIds);
        List<BrowserAccessPolicy> policies = browserPolicyRepository.findPoliciesByClassroomId(classroomId, true);
        Map<String, BrowserPolicyResolution> resolutions = new LinkedHashMap<>();
        for (String deviceId : targetDeviceIds) {
            if (context.devices().containsKey(deviceId)) {
                resolutions.put(deviceId, browserPolicyResolver.resolve(
                        new BrowserPolicyContext(
                                classroomId,
                                deviceId,
                                context.groupIdByDeviceId().get(deviceId),
                                null),
                        policies));
            }
        }

        Map<String, List<BrowserUrlRule>> rulesByPolicyId = rulesByPolicyId(resolutions.values());
        List<TargetPlan> plans = new ArrayList<>(targetDeviceIds.size());
        for (String deviceId : targetDeviceIds) {
            TargetPlan preflight = preflight(
                    deviceId,
                    context,
                    DeviceCapability.BROWSER_NAVIGATION_POLICY_V1,
                    "Device does not announce BROWSER_NAVIGATION_POLICY_V1.");
            if (!preflight.ready()) {
                plans.add(preflight);
                continue;
            }

            BrowserPolicyResolution resolution = resolutions.get(deviceId);
            List<BrowserUrlRule> rules = resolution.implicit()
                    ? List.of()
                    : rulesByPolicyId.getOrDefault(resolution.policy().policyId(), List.of());
            if (hasEnabledExactUrl(rules)) {
                plans.add(TargetPlan.blocked(
                        deviceId,
                        preflight.target(),
                        ErrorCode.BROWSER_POLICY_NOT_NATIVE_ENFORCEABLE,
                        "Browser navigation policy contains an enabled EXACT_URL rule."));
                continue;
            }

            plans.add(preflight.withNavigationParameters(navigationParameters(resolution, rules)));
        }
        return plans;
    }

    private List<TargetPlan> downloadTargetPlans(String classroomId, List<String> targetDeviceIds) {
        TargetContext context = targetContext(classroomId, targetDeviceIds);
        List<BrowserDownloadPolicy> policies =
                browserDownloadPolicyRepository.findPoliciesByClassroomId(classroomId, true);

        List<TargetPlan> plans = new ArrayList<>(targetDeviceIds.size());
        for (String deviceId : targetDeviceIds) {
            TargetPlan preflight = preflight(
                    deviceId,
                    context,
                    DeviceCapability.BROWSER_DOWNLOAD_POLICY_V1,
                    "Device does not announce BROWSER_DOWNLOAD_POLICY_V1.");
            if (!preflight.ready()) {
                plans.add(preflight);
                continue;
            }

            BrowserDownloadPolicyResolution resolution = browserDownloadPolicyResolver.resolve(
                    new BrowserDownloadPolicyContext(
                            classroomId,
                            deviceId,
                            context.groupIdByDeviceId().get(deviceId),
                            null),
                    policies);
            plans.add(preflight.withDownloadParameters(downloadParameters(resolution)));
        }
        return plans;
    }

    private TargetContext targetContext(String classroomId, List<String> targetDeviceIds) {
        Map<String, Device> devices = devicesById(deviceRepository.findByClassroomId(classroomId));
        Map<String, RegisteredNetworkDevice> bindings = bindingsByDeviceId(
                bindingRepository.findCurrentByDeviceIds(targetDeviceIds));
        Map<String, ClientConnectionSnapshot> snapshots = connectionRegistry.snapshotsByDeviceId();
        Map<UUID, KnownMasterClient> knownClients = pairingService.knownClients().stream()
                .collect(Collectors.toMap(
                        KnownMasterClient::clientNetworkIdentityId,
                        Function.identity(),
                        (left, right) -> left,
                        LinkedHashMap::new));
        Map<String, AssignmentResponse> assignments = adminRepository.findCurrentAssignmentsByDeviceIds(targetDeviceIds);
        Map<String, StudentResponse> students = adminRepository.findStudentsByIds(assignments.values().stream()
                .map(AssignmentResponse::studentId)
                .toList());
        Map<String, String> groupIdByDeviceId = new LinkedHashMap<>();
        for (AssignmentResponse assignment : assignments.values()) {
            StudentResponse student = students.get(assignment.studentId());
            if (student != null && student.groupId() != null && !student.groupId().isBlank()) {
                groupIdByDeviceId.put(assignment.deviceId(), student.groupId());
            }
        }
        return new TargetContext(devices, bindings, snapshots, knownClients, groupIdByDeviceId);
    }

    private TargetPlan preflight(
            String deviceId,
            TargetContext context,
            DeviceCapability requiredCapability,
            String missingCapabilityMessage) {
        Device device = context.devices().get(deviceId);
        OperationTarget target = new OperationTarget(
                OperationTargetType.DEVICE,
                deviceId,
                device == null ? deviceId : device.displayName());
        if (device == null) {
            return TargetPlan.blocked(
                    deviceId,
                    target,
                    ErrorCode.DEVICE_NOT_FOUND,
                    "Device does not belong to classroom.");
        }

        RegisteredNetworkDevice binding = context.bindings().get(deviceId);
        if (binding == null) {
            return TargetPlan.blocked(
                    deviceId,
                    target,
                    ErrorCode.DEVICE_NOT_REGISTERED,
                    "Device is not registered for network operations.");
        }

        TargetPlan trustBlocked = trustBlock(deviceId, target, binding, context.knownClients());
        if (trustBlocked != null) {
            return trustBlocked;
        }

        ClientConnectionSnapshot snapshot = context.snapshots().get(deviceId);
        if (!authenticatedOnline(snapshot, binding)) {
            return TargetPlan.blocked(
                    deviceId,
                    target,
                    ErrorCode.DEVICE_OFFLINE,
                    "Device is offline.");
        }

        if (!snapshot.capabilities().contains(requiredCapability)) {
            return TargetPlan.blocked(
                    deviceId,
                    target,
                    ErrorCode.CAPABILITY_NOT_SUPPORTED,
                    missingCapabilityMessage);
        }

        return TargetPlan.ready(deviceId, target, snapshot);
    }

    private TargetPlan trustBlock(
            String deviceId,
            OperationTarget target,
            RegisteredNetworkDevice binding,
            Map<UUID, KnownMasterClient> knownClients) {
        KnownMasterClient client = knownClients.get(binding.networkIdentityId());
        if (client != null && client.status() == PairingStatus.REVOKED) {
            return TargetPlan.blocked(
                    deviceId,
                    target,
                    ErrorCode.CLIENT_REVOKED,
                    "Client pairing has been revoked.");
        }

        if (client == null || client.status() != PairingStatus.PAIRED
                || !client.clientInstallationId().equals(binding.installationId())
                || !client.clientPublicKeyFingerprint().equals(binding.publicKeyFingerprint())) {
            return TargetPlan.blocked(
                    deviceId,
                    target,
                    ErrorCode.MASTER_NOT_PAIRED,
                    "Client is not paired with this Master.");
        }

        return null;
    }

    private boolean authenticatedOnline(
            ClientConnectionSnapshot snapshot,
            RegisteredNetworkDevice binding) {
        return snapshot != null
                && snapshot.registered()
                && snapshot.status() == DeviceStatus.ONLINE
                && snapshot.clientNetworkIdentityId().equals(binding.networkIdentityId())
                && snapshot.clientInstallationId().equals(binding.installationId())
                && snapshot.deviceId() != null
                && snapshot.deviceId().equals(binding.deviceId())
                && snapshot.connectionId() != null;
    }

    private Map<String, List<BrowserUrlRule>> rulesByPolicyId(Collection<BrowserPolicyResolution> resolutions) {
        List<String> policyIds = resolutions.stream()
                .filter(resolution -> !resolution.implicit())
                .map(resolution -> resolution.policy().policyId())
                .distinct()
                .sorted()
                .toList();
        Map<String, List<BrowserUrlRule>> rulesByPolicyId = new LinkedHashMap<>();
        for (String policyId : policyIds) {
            rulesByPolicyId.put(policyId, browserPolicyRepository.findRulesByPolicyId(policyId, null).stream()
                    .sorted(Comparator.comparing(BrowserUrlRule::ruleId))
                    .toList());
        }
        return rulesByPolicyId;
    }

    private boolean hasEnabledExactUrl(List<BrowserUrlRule> rules) {
        return rules.stream()
                .anyMatch(rule -> rule.enabled() && rule.matchType() == BrowserUrlMatchType.EXACT_URL);
    }

    private ApplyBrowserPolicyOperationParameters navigationParameters(
            BrowserPolicyResolution resolution,
            List<BrowserUrlRule> rules) {
        ApplyBrowserPolicyOperationParameters.Builder builder = ApplyBrowserPolicyOperationParameters.newBuilder()
                .setImplicitUnrestricted(resolution.implicit())
                .setMode(toNetworkMode(resolution.mode()))
                .setAccountScope(com.galtek.classroom.network.v1.BrowserPolicyAccountScope
                        .BROWSER_POLICY_ACCOUNT_SCOPE_ANY);
        if (!resolution.implicit()) {
            builder.setPolicyId(resolution.policy().policyId())
                    .setPolicyVersion(resolution.policy().version())
                    .setAccountScope(toNetworkAccountScope(resolution.policy().accountScope()));
            for (BrowserUrlRule rule : rules) {
                builder.addRules(BrowserPolicyRuleParameters.newBuilder()
                        .setRuleId(rule.ruleId())
                        .setAction(toNetworkRuleAction(rule.action()))
                        .setMatchType(toNetworkRuleMatchType(rule.matchType()))
                        .setPattern(rule.pattern())
                        .setEnabled(rule.enabled())
                        .build());
            }
        }
        return builder.build();
    }

    private ApplyBrowserDownloadPolicyOperationParameters downloadParameters(
            BrowserDownloadPolicyResolution resolution) {
        ApplyBrowserDownloadPolicyOperationParameters.Builder builder =
                ApplyBrowserDownloadPolicyOperationParameters.newBuilder()
                        .setImplicitNoSpecialRestrictions(resolution.implicit())
                        .setRestrictionMode(toNetworkDownloadMode(resolution.restrictionMode()))
                        .setAccountScope(com.galtek.classroom.network.v1.BrowserPolicyAccountScope
                                .BROWSER_POLICY_ACCOUNT_SCOPE_ANY);
        if (!resolution.implicit()) {
            builder.setPolicyId(resolution.policy().policyId())
                    .setPolicyVersion(resolution.policy().version())
                    .setAccountScope(toNetworkAccountScope(resolution.policy().accountScope()));
        }
        return builder.build();
    }

    private com.galtek.classroom.network.v1.BrowserPolicyMode toNetworkMode(BrowserPolicyMode mode) {
        return switch (mode) {
            case UNRESTRICTED -> com.galtek.classroom.network.v1.BrowserPolicyMode
                    .BROWSER_POLICY_MODE_UNRESTRICTED;
            case BLOCKLIST -> com.galtek.classroom.network.v1.BrowserPolicyMode
                    .BROWSER_POLICY_MODE_BLOCKLIST;
            case ALLOWLIST -> com.galtek.classroom.network.v1.BrowserPolicyMode
                    .BROWSER_POLICY_MODE_ALLOWLIST;
        };
    }

    private com.galtek.classroom.network.v1.BrowserPolicyAccountScope toNetworkAccountScope(
            BrowserPolicyAccountScope accountScope) {
        return switch (accountScope) {
            case ANY -> com.galtek.classroom.network.v1.BrowserPolicyAccountScope
                    .BROWSER_POLICY_ACCOUNT_SCOPE_ANY;
            case PRIMARY -> com.galtek.classroom.network.v1.BrowserPolicyAccountScope
                    .BROWSER_POLICY_ACCOUNT_SCOPE_PRIMARY;
            case SECONDARY -> com.galtek.classroom.network.v1.BrowserPolicyAccountScope
                    .BROWSER_POLICY_ACCOUNT_SCOPE_SECONDARY;
        };
    }

    private com.galtek.classroom.network.v1.BrowserPolicyRuleAction toNetworkRuleAction(
            BrowserUrlRuleAction action) {
        return switch (action) {
            case ALLOW -> com.galtek.classroom.network.v1.BrowserPolicyRuleAction
                    .BROWSER_POLICY_RULE_ACTION_ALLOW;
            case BLOCK -> com.galtek.classroom.network.v1.BrowserPolicyRuleAction
                    .BROWSER_POLICY_RULE_ACTION_BLOCK;
        };
    }

    private com.galtek.classroom.network.v1.BrowserPolicyRuleMatchType toNetworkRuleMatchType(
            BrowserUrlMatchType matchType) {
        return switch (matchType) {
            case HOST_EXACT -> com.galtek.classroom.network.v1.BrowserPolicyRuleMatchType
                    .BROWSER_POLICY_RULE_MATCH_TYPE_HOST_EXACT;
            case HOST_SUFFIX -> com.galtek.classroom.network.v1.BrowserPolicyRuleMatchType
                    .BROWSER_POLICY_RULE_MATCH_TYPE_HOST_SUFFIX;
            case URL_PREFIX -> com.galtek.classroom.network.v1.BrowserPolicyRuleMatchType
                    .BROWSER_POLICY_RULE_MATCH_TYPE_URL_PREFIX;
            case EXACT_URL -> com.galtek.classroom.network.v1.BrowserPolicyRuleMatchType
                    .BROWSER_POLICY_RULE_MATCH_TYPE_EXACT_URL;
        };
    }

    private com.galtek.classroom.network.v1.BrowserDownloadRestrictionMode toNetworkDownloadMode(
            BrowserDownloadRestrictionMode mode) {
        return switch (mode) {
            case NO_SPECIAL_RESTRICTIONS -> com.galtek.classroom.network.v1.BrowserDownloadRestrictionMode
                    .BROWSER_DOWNLOAD_RESTRICTION_MODE_NO_SPECIAL_RESTRICTIONS;
            case BLOCK_DANGEROUS -> com.galtek.classroom.network.v1.BrowserDownloadRestrictionMode
                    .BROWSER_DOWNLOAD_RESTRICTION_MODE_BLOCK_DANGEROUS;
            case BLOCK_POTENTIALLY_DANGEROUS -> com.galtek.classroom.network.v1.BrowserDownloadRestrictionMode
                    .BROWSER_DOWNLOAD_RESTRICTION_MODE_BLOCK_POTENTIALLY_DANGEROUS;
            case BLOCK_ALL -> com.galtek.classroom.network.v1.BrowserDownloadRestrictionMode
                    .BROWSER_DOWNLOAD_RESTRICTION_MODE_BLOCK_ALL;
            case BLOCK_MALICIOUS -> com.galtek.classroom.network.v1.BrowserDownloadRestrictionMode
                    .BROWSER_DOWNLOAD_RESTRICTION_MODE_BLOCK_MALICIOUS;
        };
    }

    private RemoteOperationOutcome awaitOutcome(DispatchHandle handle, long deadlineNanos) {
        long remainingNanos = deadlineNanos - System.nanoTime();
        if (remainingNanos <= 0) {
            return remoteOperationGateway.timeout(handle);
        }

        try {
            return handle.completion().get(remainingNanos, TimeUnit.NANOSECONDS);
        } catch (TimeoutException exception) {
            return remoteOperationGateway.timeout(handle);
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            return remoteOperationGateway.timeout(handle);
        } catch (ExecutionException exception) {
            return remoteOperationGateway.timeout(handle);
        }
    }

    private List<String> targetDeviceIdsFrom(Map<String, Object> request) {
        requireKnownBody(request);
        Object value = request.get("targetDeviceIds");
        if (!(value instanceof List<?> rawTargets) || rawTargets.isEmpty()) {
            throw validation("targetDeviceIds is required and must not be empty.");
        }

        List<String> targetDeviceIds = new ArrayList<>(rawTargets.size());
        LinkedHashSet<String> unique = new LinkedHashSet<>();
        for (Object rawTarget : rawTargets) {
            if (!(rawTarget instanceof String targetId) || targetId.isBlank()) {
                throw validation("targetDeviceIds must contain non-empty strings.");
            }

            String cleanTargetId = targetId.trim();
            if (!unique.add(cleanTargetId)) {
                throw validation("targetDeviceIds must not contain duplicates.");
            }
            targetDeviceIds.add(cleanTargetId);
        }

        return targetDeviceIds;
    }

    private void requireKnownBody(Map<String, Object> request) {
        if (request == null) {
            throw validation("Request body is required.");
        }
        for (String field : request.keySet()) {
            if (!REQUEST_FIELDS.contains(field)) {
                throw validation("Request contains unsupported field: " + field + ".");
            }
        }
    }

    private ClassroomResponse classroomOr404(String classroomId) {
        String cleanId = required(classroomId, "classroomId");
        return adminRepository.findClassroom(cleanId)
                .orElseThrow(() -> notFound(ErrorCode.CLASSROOM_NOT_FOUND, "Classroom was not found."));
    }

    private Map<String, Device> devicesById(List<Device> devices) {
        Map<String, Device> devicesById = new LinkedHashMap<>();
        for (Device device : devices) {
            devicesById.putIfAbsent(device.deviceId(), device);
        }
        return devicesById;
    }

    private Map<String, RegisteredNetworkDevice> bindingsByDeviceId(List<RegisteredNetworkDevice> bindings) {
        Map<String, RegisteredNetworkDevice> bindingsByDeviceId = new LinkedHashMap<>();
        for (RegisteredNetworkDevice binding : bindings) {
            bindingsByDeviceId.putIfAbsent(binding.deviceId(), binding);
        }
        return bindingsByDeviceId;
    }

    private BatchTargetResult failed(
            OperationTarget target,
            ErrorCode errorCode,
            String message,
            int attempt) {
        return new BatchTargetResult(
                target,
                TargetExecutionStatus.FAILED,
                errorCode,
                message,
                attempt);
    }

    private String required(String value, String fieldName) {
        if (value == null || value.isBlank()) {
            throw validation(fieldName + " is required.");
        }
        return value.trim();
    }

    private MasterAuthorizationResponse requireAuthorizedAndStorage() {
        MasterAuthorizationResponse authorization = masterAccessGuard.requireAuthorized();
        MasterStorageHealth health = storageState.health();
        if (health.status() != MasterStorageStatus.READY) {
            String code = health.errorCode() == null
                    ? ErrorCode.MASTER_DATABASE_UNAVAILABLE.name()
                    : health.errorCode();
            throw new ApiException(
                    HttpStatus.SERVICE_UNAVAILABLE,
                    code,
                    "Master storage is unavailable.");
        }

        return authorization;
    }

    private OffsetDateTime nowUtc() {
        return OffsetDateTime.ofInstant(clock.instant(), ZoneOffset.UTC);
    }

    private ApiException notFound(ErrorCode errorCode, String message) {
        return new ApiException(HttpStatus.NOT_FOUND, errorCode, message);
    }

    private ApiException validation(String message) {
        return new ApiException(HttpStatus.BAD_REQUEST, ErrorCode.INVALID_REQUEST, message);
    }

    private record TargetContext(
            Map<String, Device> devices,
            Map<String, RegisteredNetworkDevice> bindings,
            Map<String, ClientConnectionSnapshot> snapshots,
            Map<UUID, KnownMasterClient> knownClients,
            Map<String, String> groupIdByDeviceId) {
    }

    private record TargetPlan(
            String deviceId,
            OperationTarget target,
            ClientConnectionSnapshot snapshot,
            ApplyBrowserPolicyOperationParameters navigationParameters,
            ApplyBrowserDownloadPolicyOperationParameters downloadParameters,
            ErrorCode errorCode,
            String message) {

        static TargetPlan ready(
                String deviceId,
                OperationTarget target,
                ClientConnectionSnapshot snapshot) {
            return new TargetPlan(deviceId, target, snapshot, null, null, null, null);
        }

        static TargetPlan blocked(
                String deviceId,
                OperationTarget target,
                ErrorCode errorCode,
                String message) {
            return new TargetPlan(deviceId, target, null, null, null, errorCode, message);
        }

        TargetPlan withNavigationParameters(ApplyBrowserPolicyOperationParameters parameters) {
            return new TargetPlan(deviceId, target, snapshot, parameters, null, null, null);
        }

        TargetPlan withDownloadParameters(ApplyBrowserDownloadPolicyOperationParameters parameters) {
            return new TargetPlan(deviceId, target, snapshot, null, parameters, null, null);
        }

        boolean ready() {
            return errorCode == null;
        }

        BatchTargetResult intentResult() {
            return ready()
                    ? new BatchTargetResult(target, TargetExecutionStatus.PENDING, null, "Dispatch pending.", 1)
                    : finalFailure();
        }

        BatchTargetResult finalFailure() {
            return new BatchTargetResult(target, TargetExecutionStatus.FAILED, errorCode, message, 1);
        }
    }
}
