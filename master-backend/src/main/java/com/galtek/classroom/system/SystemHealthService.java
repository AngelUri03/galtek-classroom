package com.galtek.classroom.system;

import org.springframework.stereotype.Service;

@Service
public class SystemHealthService {

    private static final String APPLICATION_NAME = "Galtek Classroom Master";
    private static final String STATUS_UP = "UP";

    public SystemHealthResponse currentHealth() {
        return new SystemHealthResponse(APPLICATION_NAME, STATUS_UP);
    }
}
