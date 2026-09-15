package com.galtek.classroom.credentialvault;

import com.galtek.classroom.api.ApiException;
import com.galtek.classroom.master.MasterAccessGuard;
import com.galtek.classroom.operations.ErrorCode;
import java.nio.ByteBuffer;
import java.nio.CharBuffer;
import java.nio.charset.CharacterCodingException;
import java.nio.charset.CodingErrorAction;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.http.HttpStatus;
import org.springframework.http.MediaType;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping("/api/credential-vault")
@ConditionalOnProperty(
        prefix = "galtek.classroom.master.storage",
        name = "enabled",
        havingValue = "true",
        matchIfMissing = true)
public class CredentialVaultController {

    public static final String VAULT_SESSION_HEADER = "X-Galtek-Vault-Session";
    private static final int MAX_MASTER_PASSWORD_BYTES = 16 * 1024;

    private final MasterAccessGuard masterAccessGuard;
    private final CredentialVaultService credentialVaultService;

    public CredentialVaultController(
            MasterAccessGuard masterAccessGuard,
            CredentialVaultService credentialVaultService) {
        this.masterAccessGuard = masterAccessGuard;
        this.credentialVaultService = credentialVaultService;
    }

    @GetMapping("/status")
    public CredentialVaultStatusResponse status() {
        masterAccessGuard.requireAuthorized();
        CredentialVaultService.CredentialVaultStatus status = credentialVaultService.status();
        return new CredentialVaultStatusResponse(status.initialized(), status.locked());
    }

    @PostMapping(path = "/initialize", consumes = MediaType.APPLICATION_OCTET_STREAM_VALUE)
    public CredentialVaultStatusResponse initialize(@RequestBody byte[] body) {
        masterAccessGuard.requireAuthorized();
        String masterPassword = decodeUtf8Secret(body, MAX_MASTER_PASSWORD_BYTES);
        try {
            credentialVaultService.initialize(masterPassword);
            CredentialVaultService.CredentialVaultStatus status = credentialVaultService.status();
            return new CredentialVaultStatusResponse(status.initialized(), status.locked());
        } finally {
            clear(body);
        }
    }

    @PostMapping(path = "/unlock", consumes = MediaType.APPLICATION_OCTET_STREAM_VALUE)
    public CredentialVaultUnlockResponse unlock(@RequestBody byte[] body) {
        masterAccessGuard.requireAuthorized();
        String masterPassword = decodeUtf8Secret(body, MAX_MASTER_PASSWORD_BYTES);
        try {
            CredentialVaultSession session = credentialVaultService.unlock(masterPassword);
            return new CredentialVaultUnlockResponse(
                    true,
                    false,
                    session.token(),
                    session.expiresAfterLastAccess().toString());
        } finally {
            clear(body);
        }
    }

    @PostMapping("/lock")
    public CredentialVaultStatusResponse lock(
            @RequestHeader(VAULT_SESSION_HEADER) String vaultSessionToken) {
        masterAccessGuard.requireAuthorized();
        credentialVaultService.lock(vaultSessionToken);
        CredentialVaultService.CredentialVaultStatus status = credentialVaultService.status();
        return new CredentialVaultStatusResponse(status.initialized(), status.locked());
    }

    public static String decodeUtf8Secret(byte[] body, int maxBytes) {
        if (body == null || body.length == 0 || body.length > maxBytes) {
            throw new ApiException(
                    HttpStatus.BAD_REQUEST,
                    ErrorCode.INVALID_REQUEST,
                    "Secret body is required as bounded UTF-8 octets.");
        }
        if (body.length >= 3
                && (body[0] & 0xff) == 0xef
                && (body[1] & 0xff) == 0xbb
                && (body[2] & 0xff) == 0xbf) {
            throw new ApiException(
                    HttpStatus.BAD_REQUEST,
                    ErrorCode.INVALID_REQUEST,
                    "Secret body must be UTF-8 without BOM.");
        }

        try {
            CharBuffer decoded = StandardCharsets.UTF_8
                    .newDecoder()
                    .onMalformedInput(CodingErrorAction.REPORT)
                    .onUnmappableCharacter(CodingErrorAction.REPORT)
                    .decode(ByteBuffer.wrap(body));
            return decoded.toString();
        } catch (CharacterCodingException exception) {
            throw new ApiException(
                    HttpStatus.BAD_REQUEST,
                    ErrorCode.INVALID_REQUEST,
                    "Secret body must be valid UTF-8.");
        }
    }

    public static void clear(byte[] body) {
        if (body != null) {
            Arrays.fill(body, (byte) 0);
        }
    }

    public record CredentialVaultStatusResponse(
            boolean initialized,
            boolean locked) {
    }

    public record CredentialVaultUnlockResponse(
            boolean initialized,
            boolean locked,
            String vaultSessionToken,
            String expiresAtUtc) {
    }
}
