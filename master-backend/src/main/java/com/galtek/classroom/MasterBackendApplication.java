package com.galtek.classroom;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.galtek.classroom.performance.RuntimeDiagnosticsSnapshot;
import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;

@SpringBootApplication
public class MasterBackendApplication {

    private static final String RUNTIME_DIAGNOSTICS_ARGUMENT = "--runtime-diagnostics";

    public static void main(String[] args) {
        if (hasRuntimeDiagnosticsArgument(args)) {
            printRuntimeDiagnostics();
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
}
