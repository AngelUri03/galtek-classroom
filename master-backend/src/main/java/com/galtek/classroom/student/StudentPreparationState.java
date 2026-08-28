package com.galtek.classroom.student;

import static com.galtek.classroom.domain.DomainChecks.copyList;
import static com.galtek.classroom.domain.DomainChecks.requireNonBlank;
import static com.galtek.classroom.domain.DomainChecks.requireNonNull;

import com.galtek.classroom.operations.ErrorCode;
import java.util.List;

public record StudentPreparationState(
        String deviceId,
        String studentId,
        StudentPreparationStage stage,
        StudentPreparationStatus status,
        List<ErrorCode> errors) {

    public StudentPreparationState {
        deviceId = requireNonBlank(deviceId, "deviceId");
        studentId = requireNonBlank(studentId, "studentId");
        requireNonNull(stage, "stage");
        requireNonNull(status, "status");
        errors = copyList(errors, "errors");

        if (status == StudentPreparationStatus.READY && stage != StudentPreparationStage.READY) {
            throw new IllegalArgumentException("READY status requires READY stage.");
        }
        if (status == StudentPreparationStatus.READY && !errors.isEmpty()) {
            throw new IllegalArgumentException("READY preparation cannot contain errors.");
        }
        if ((status == StudentPreparationStatus.PARTIAL_READY
                || status == StudentPreparationStatus.RECOVERY_REQUIRED
                || status == StudentPreparationStatus.FAILED)
                && errors.isEmpty()) {
            throw new IllegalArgumentException("Problem preparation states require at least one error.");
        }
    }

    public static StudentPreparationState assigned(String deviceId, String studentId) {
        return new StudentPreparationState(
                deviceId,
                studentId,
                StudentPreparationStage.ASSIGNED,
                StudentPreparationStatus.PENDING,
                List.of());
    }

    public static StudentPreparationState inProgress(
            String deviceId,
            String studentId,
            StudentPreparationStage stage) {
        if (stage == StudentPreparationStage.READY) {
            throw new IllegalArgumentException("Use ready() for READY preparation state.");
        }

        return new StudentPreparationState(
                deviceId,
                studentId,
                stage,
                StudentPreparationStatus.IN_PROGRESS,
                List.of());
    }

    public static StudentPreparationState ready(String deviceId, String studentId) {
        return new StudentPreparationState(
                deviceId,
                studentId,
                StudentPreparationStage.READY,
                StudentPreparationStatus.READY,
                List.of());
    }

    public static StudentPreparationState partialReady(
            String deviceId,
            String studentId,
            StudentPreparationStage stage,
            List<ErrorCode> errors) {
        return new StudentPreparationState(
                deviceId,
                studentId,
                stage,
                StudentPreparationStatus.PARTIAL_READY,
                errors);
    }

    public static StudentPreparationState recoveryRequired(
            String deviceId,
            String studentId,
            StudentPreparationStage stage,
            List<ErrorCode> errors) {
        return new StudentPreparationState(
                deviceId,
                studentId,
                stage,
                StudentPreparationStatus.RECOVERY_REQUIRED,
                errors);
    }

    public static StudentPreparationState failed(
            String deviceId,
            String studentId,
            StudentPreparationStage stage,
            List<ErrorCode> errors) {
        return new StudentPreparationState(
                deviceId,
                studentId,
                stage,
                StudentPreparationStatus.FAILED,
                errors);
    }

    public boolean readyForClassActivity() {
        return status.ready();
    }

    public boolean stillPreparing() {
        return status.preparing();
    }
}
