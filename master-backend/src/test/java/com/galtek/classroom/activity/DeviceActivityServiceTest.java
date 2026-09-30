package com.galtek.classroom.activity;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import com.galtek.classroom.device.Device;
import com.galtek.classroom.device.DeviceRepository;
import com.galtek.classroom.device.DeviceStatus;
import com.galtek.classroom.master.MasterAccessGuard;
import java.time.Clock;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.List;
import java.util.Set;
import org.junit.jupiter.api.Test;
import org.mockito.ArgumentCaptor;

class DeviceActivityServiceTest {
    @Test
    void todayReadIsAuthorizedDeviceScopedUtcAndLimited() {
        MasterAccessGuard guard = mock(MasterAccessGuard.class);
        DeviceRepository devices = mock(DeviceRepository.class);
        DeviceActivityRepository repository = mock(DeviceActivityRepository.class);
        Clock clock = Clock.fixed(Instant.parse("2026-09-28T18:30:00Z"), ZoneOffset.UTC);
        Device device = new Device("device-1", "installation-1", "PC01", "pc01",
                DeviceStatus.ONLINE, OffsetDateTime.parse("2026-09-28T18:00:00Z"), Set.of(), null);
        when(devices.findByClassroomId("room-1")).thenReturn(List.of(device));
        when(repository.find(org.mockito.ArgumentMatchers.anyString(), org.mockito.ArgumentMatchers.anyString(),
                org.mockito.ArgumentMatchers.any(), org.mockito.ArgumentMatchers.any(),
                org.mockito.ArgumentMatchers.anyInt())).thenReturn(List.of());

        DeviceActivityService service = new DeviceActivityService(guard, devices, repository, clock);
        assertThat(service.read("room-1", "device-1", "today", 500)).isEmpty();

        verify(guard).requireAuthorized();
        ArgumentCaptor<OffsetDateTime> from = ArgumentCaptor.forClass(OffsetDateTime.class);
        ArgumentCaptor<OffsetDateTime> to = ArgumentCaptor.forClass(OffsetDateTime.class);
        verify(repository).find(org.mockito.ArgumentMatchers.eq("room-1"),
                org.mockito.ArgumentMatchers.eq("device-1"), from.capture(), to.capture(),
                org.mockito.ArgumentMatchers.eq(50));
        assertThat(from.getValue()).isEqualTo(OffsetDateTime.parse("2026-09-28T00:00:00Z"));
        assertThat(to.getValue()).isEqualTo(OffsetDateTime.parse("2026-09-29T00:00:00Z"));
    }
}
