package com.galtek.classroom.application;

import java.time.Clock;
import java.time.OffsetDateTime;
import java.util.List;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class ApplicationCatalogService {

    private final ApplicationDefinitionRepository applicationDefinitionRepository;
    private final Clock clock;

    public ApplicationCatalogService(
            ApplicationDefinitionRepository applicationDefinitionRepository,
            Clock clock) {
        this.applicationDefinitionRepository = applicationDefinitionRepository;
        this.clock = clock;
    }

    @Transactional
    public ApplicationDefinition create(ApplicationDefinition applicationDefinition) {
        applicationDefinitionRepository.create(applicationDefinition, nowUtc());
        return applicationDefinition;
    }

    public List<ApplicationDefinition> activeApplications() {
        return applicationDefinitionRepository.findActive();
    }

    private OffsetDateTime nowUtc() {
        return OffsetDateTime.now(clock);
    }
}
