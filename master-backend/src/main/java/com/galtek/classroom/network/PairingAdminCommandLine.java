package com.galtek.classroom.network;

import java.util.ArrayList;
import java.util.List;

public record PairingAdminCommandLine(
        PairingAdminCommandMode mode,
        String[] hostArgs,
        String descriptorInputPath,
        String challengeOutputPath,
        String responseInputPath,
        boolean approvePairingIntent,
        String errorMessage) {

    public boolean valid() {
        return errorMessage == null;
    }

    public boolean isPairingCommand() {
        return mode != PairingAdminCommandMode.NONE || errorMessage != null;
    }

    public static PairingAdminCommandLine parse(String[] args) {
        final String createChallengeArgument = "--pairing-create-challenge";
        final String challengeOutArgument = "--pairing-challenge-out";
        final String approveIntentArgument = "--approve-pairing-intent";
        final String completeArgument = "--pairing-complete";

        PairingAdminCommandMode mode = PairingAdminCommandMode.NONE;
        List<String> hostArgs = new ArrayList<>();
        String descriptorInputPath = null;
        String challengeOutputPath = null;
        String responseInputPath = null;
        boolean approvePairingIntent = false;
        String error = null;

        if (args == null) {
            args = new String[0];
        }

        for (int index = 0; index < args.length; index++) {
            String argument = args[index];
            if (createChallengeArgument.equalsIgnoreCase(argument)) {
                if (mode != PairingAdminCommandMode.NONE && mode != PairingAdminCommandMode.CREATE_CHALLENGE) {
                    error = firstError(error, "Only one pairing command mode can be used.");
                }
                mode = PairingAdminCommandMode.CREATE_CHALLENGE;
                if (index + 1 >= args.length) {
                    error = firstError(error, "--pairing-create-challenge requires a descriptor file path.");
                    continue;
                }
                descriptorInputPath = args[++index];
                continue;
            }

            if (challengeOutArgument.equalsIgnoreCase(argument)) {
                if (index + 1 >= args.length) {
                    error = firstError(error, "--pairing-challenge-out requires an output file path.");
                    continue;
                }
                challengeOutputPath = args[++index];
                continue;
            }

            if (approveIntentArgument.equalsIgnoreCase(argument)) {
                approvePairingIntent = true;
                continue;
            }

            if (completeArgument.equalsIgnoreCase(argument)) {
                if (mode != PairingAdminCommandMode.NONE && mode != PairingAdminCommandMode.COMPLETE) {
                    error = firstError(error, "Only one pairing command mode can be used.");
                }
                mode = PairingAdminCommandMode.COMPLETE;
                if (index + 1 >= args.length) {
                    error = firstError(error, "--pairing-complete requires a response file path.");
                    continue;
                }
                responseInputPath = args[++index];
                continue;
            }

            hostArgs.add(argument);
        }

        if (challengeOutputPath != null && mode != PairingAdminCommandMode.CREATE_CHALLENGE) {
            error = firstError(error, "--pairing-challenge-out can only be used with --pairing-create-challenge.");
        }
        if (approvePairingIntent && mode != PairingAdminCommandMode.CREATE_CHALLENGE) {
            error = firstError(error, "--approve-pairing-intent can only be used with --pairing-create-challenge.");
        }
        if (mode == PairingAdminCommandMode.CREATE_CHALLENGE && challengeOutputPath == null) {
            error = firstError(error, "--pairing-create-challenge requires --pairing-challenge-out.");
        }

        return new PairingAdminCommandLine(
                mode,
                hostArgs.toArray(String[]::new),
                descriptorInputPath,
                challengeOutputPath,
                responseInputPath,
                approvePairingIntent,
                error);
    }

    private static String firstError(String current, String candidate) {
        return current == null ? candidate : current;
    }
}
