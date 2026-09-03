package com.galtek.classroom.application;

import java.time.OffsetDateTime;
import java.util.List;
import java.util.Optional;

public interface ApplicationDefinitionRepository {

    void create(ApplicationDefinition application, OffsetDateTime nowUtc);

    Optional<ApplicationDefinition> findById(String applicationId);

    Optional<ApplicationDefinition> findActiveById(String applicationId);

    List<ApplicationDefinition> findActive();
}
