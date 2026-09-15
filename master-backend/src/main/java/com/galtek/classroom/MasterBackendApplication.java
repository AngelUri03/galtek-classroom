package com.galtek.classroom;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.galtek.classroom.network.PairingAdminCommandLine;
import com.galtek.classroom.network.PairingAdminCommandMode;
import com.galtek.classroom.network.PairingFileBootstrapService;
import com.galtek.classroom.performance.RuntimeDiagnosticsSnapshot;
import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.boot.builder.SpringApplicationBuilder;
import org.springframework.boot.WebApplicationType;
import org.springframework.context.ConfigurableApplicationContext;

@SpringBootApplication
public class MasterBackendApplication {

    private static final String RUNTIME_DIAGNOSTICS_ARGUMENT = "--runtime-diagnostics";

    public static void main(String[] args) {
        if (hasRuntimeDiagnosticsArgument(args)) {
            printRuntimeDiagnostics();
            return;
        }

        PairingAdminCommandLine pairingCommand = PairingAdminCommandLine.parse(args);
        if (pairingCommand.isPairingCommand()) {
            runPairingCommand(pairingCommand);
            return;
        }

        if (System.getProperty("debug") == null) {
            System.setProperty("debug", "false");
        }

        SpringApplication.run(MasterBackendApplication.class, args);
    }

    private static boolean hasRuntimeDiagnosticsArgument(String[] args) {
        if (args == null) {
            return false;
        }

        for (String argument : args) {
            if (RUNTIME_DIAGNOSTICS_ARGUMENT.equalsIgnoreCase(argument)) {
                return true;
            }
        }

        return false;
    }

    private static void printRuntimeDiagnostics() {
        try {
            System.out.println(new ObjectMapper()
                    .writerWithDefaultPrettyPrinter()
                    .writeValueAsString(RuntimeDiagnosticsSnapshot.capture(
                            "Galtek Classroom Master Backend")));
        } catch (JsonProcessingException exception) {
            throw new IllegalStateException("Runtime diagnostics could not be serialized.", exception);
        }
    }

    private static void runPairingCommand(PairingAdminCommandLine command) {
        if (!command.valid()) {
            System.err.println(command.errorMessage());
            System.exit(2);
            return;
        }

        if (System.getProperty("debug") == null) {
            System.setProperty("debug", "false");
        }

        try (ConfigurableApplicationContext context = new SpringApplicationBuilder(MasterBackendApplication.class)
                .web(WebApplicationType.NONE)
                .properties(
                        "spring.main.banner-mode=off",
                        "logging.level.root=OFF")
                .run(command.hostArgs())) {
            PairingFileBootstrapService bootstrap = context.getBean(PairingFileBootstrapService.class);
            var result = command.mode() == PairingAdminCommandMode.CREATE_CHALLENGE
                    ? bootstrap.createChallenge(
                            command.descriptorInputPath(),
                            command.challengeOutputPath(),
                            command.approvePairingIntent())
                    : bootstrap.completePairing(command.responseInputPath());

            System.out.println(bootstrap.serializeResult(result));
            if (!result.succeeded()) {
                System.exit(1);
            }
        } catch (Exception exception) {
            System.err.println("Pairing command failed: " + exception.getMessage());
            System.exit(1);
        }
    }
}
