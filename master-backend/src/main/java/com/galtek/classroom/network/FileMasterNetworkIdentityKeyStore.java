package com.galtek.classroom.network;

import java.io.DataInputStream;
import java.io.DataOutputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.channels.FileChannel;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.nio.file.StandardOpenOption;
import java.security.GeneralSecurityException;
import java.security.KeyFactory;
import java.security.KeyPairGenerator;
import java.security.PrivateKey;
import java.security.PublicKey;
import java.security.SecureRandom;
import java.security.Signature;
import java.security.spec.PKCS8EncodedKeySpec;
import java.security.spec.X509EncodedKeySpec;
import java.time.Clock;
import java.util.Base64;
import javax.crypto.Cipher;
import javax.crypto.spec.GCMParameterSpec;
import javax.crypto.spec.SecretKeySpec;
import org.bouncycastle.asn1.x509.KeyPurposeId;

public class FileMasterNetworkIdentityKeyStore implements MasterNetworkIdentityKeyStore {

    private static final String MAGIC = "GTK-MNK1";
    private static final int VERSION = 1;
    private static final int PROTECTOR_BYTES = 32;
    private static final int GCM_IV_BYTES = 12;
    private static final int GCM_TAG_BITS = 128;

    private final Path dataDirectory;
    private final Path privateKeyFile;
    private final Path protectorFile;
    private final SecureRandom secureRandom;

    public FileMasterNetworkIdentityKeyStore(Path dataDirectory) {
        this(dataDirectory, new SecureRandom());
    }

    FileMasterNetworkIdentityKeyStore(Path dataDirectory, SecureRandom secureRandom) {
        this.dataDirectory = dataDirectory.toAbsolutePath().normalize();
        this.privateKeyFile = this.dataDirectory.resolve(PairingConstants.MASTER_NETWORK_PRIVATE_KEY_FILE_NAME);
        this.protectorFile = this.dataDirectory.resolve(PairingConstants.MASTER_NETWORK_PROTECTOR_FILE_NAME);
        this.secureRandom = secureRandom;
    }

    @Override
    public boolean hasAnyKeyMaterial() {
        return Files.exists(privateKeyFile) || Files.exists(protectorFile);
    }

    @Override
    public MasterNetworkKeyCreationResult create(String keyId) {
        if (!NetworkIdentityCrypto.isValidSha256Hex(keyId)) {
            return MasterNetworkKeyCreationResult.failed("Master Network Identity keyId is invalid.");
        }

        if (hasAnyKeyMaterial()) {
            return MasterNetworkKeyCreationResult.alreadyExists();
        }

        try {
            Files.createDirectories(dataDirectory);

            KeyPairGenerator generator = KeyPairGenerator.getInstance("RSA");
            generator.initialize(PairingConstants.RSA_KEY_SIZE_BITS, secureRandom);
            var keyPair = generator.generateKeyPair();
            byte[] publicKey = keyPair.getPublic().getEncoded();
            byte[] protector = randomBytes(PROTECTOR_BYTES);
            byte[] iv = randomBytes(GCM_IV_BYTES);
            byte[] ciphertext = encryptPrivateKey(keyId, protector, iv, keyPair.getPrivate().getEncoded());

            Path protectorTemp = temporaryPath(protectorFile);
            Path keyTemp = temporaryPath(privateKeyFile);
            try {
                writeBytesDurably(protectorTemp, protector);
                LocalFileSecurity.restrictOwnerOnly(protectorTemp);
                writePrivateKeyFile(keyTemp, keyId, publicKey, iv, ciphertext);
                LocalFileSecurity.restrictOwnerOnly(keyTemp);

                Files.move(protectorTemp, protectorFile, StandardCopyOption.ATOMIC_MOVE);
                Files.move(keyTemp, privateKeyFile, StandardCopyOption.ATOMIC_MOVE);
                LocalFileSecurity.restrictOwnerOnly(protectorFile);
                LocalFileSecurity.restrictOwnerOnly(privateKeyFile);
                forceDirectory(dataDirectory);
            } finally {
                Files.deleteIfExists(protectorTemp);
                Files.deleteIfExists(keyTemp);
            }

            return MasterNetworkKeyCreationResult.created(
                    NetworkIdentityCrypto.fingerprint(publicKey),
                    Base64.getEncoder().encodeToString(publicKey));
        } catch (IOException | GeneralSecurityException exception) {
            return MasterNetworkKeyCreationResult.failed(
                    "Master Network Identity key material could not be created: " + exception.getMessage());
        }
    }

    @Override
    public MasterNetworkKeyLookupResult lookup(String keyId) {
        if (!Files.exists(privateKeyFile) || !Files.exists(protectorFile)) {
            return MasterNetworkKeyLookupResult.missing();
        }

        try {
            LoadedMasterNetworkKey loaded = loadPrivateKey(keyId);
            return MasterNetworkKeyLookupResult.found(
                    loaded.publicKeyFingerprint(),
                    loaded.subjectPublicKeyInfoBase64());
        } catch (IOException | GeneralSecurityException | IllegalArgumentException exception) {
            return MasterNetworkKeyLookupResult.invalid(
                    "Master Network Identity key material could not be read: " + exception.getMessage());
        }
    }

    @Override
    public MasterNetworkSignatureResult sign(String keyId, byte[] data) {
        if (!Files.exists(privateKeyFile) || !Files.exists(protectorFile)) {
            return MasterNetworkSignatureResult.missing();
        }

        try {
            LoadedMasterNetworkKey loaded = loadPrivateKey(keyId);
            Signature signature = Signature.getInstance("SHA256withRSA");
            signature.initSign(loaded.privateKey(), secureRandom);
            signature.update(data);
            return MasterNetworkSignatureResult.signed(Base64.getEncoder().encodeToString(signature.sign()));
        } catch (IOException | GeneralSecurityException | IllegalArgumentException exception) {
            return MasterNetworkSignatureResult.invalid(
                    "Master Network Identity key material could not sign data: " + exception.getMessage());
        }
    }

    @Override
    public MasterNetworkTlsIdentityResult tlsIdentity(String keyId) {
        if (!Files.exists(privateKeyFile) || !Files.exists(protectorFile)) {
            return MasterNetworkTlsIdentityResult.missing();
        }

        try {
            LoadedMasterNetworkKey loaded = loadPrivateKey(keyId);
            var certificate = NetworkIdentityCertificateFactory.createSelfSigned(
                    loaded.publicKey(),
                    loaded.privateKey(),
                    "Galtek Classroom Master " + loaded.publicKeyFingerprint(),
                    Clock.systemUTC(),
                    secureRandom,
                    KeyPurposeId.id_kp_serverAuth);
            return MasterNetworkTlsIdentityResult.ready(
                    loaded.publicKeyFingerprint(),
                    certificate,
                    loaded.privateKey());
        } catch (IOException | GeneralSecurityException | IllegalArgumentException exception) {
            return MasterNetworkTlsIdentityResult.invalid(
                    "Master Network Identity TLS material could not be read: " + exception.getMessage());
        }
    }

    private LoadedMasterNetworkKey loadPrivateKey(String expectedKeyId)
            throws IOException, GeneralSecurityException {
        byte[] protector = Files.readAllBytes(protectorFile);
        if (protector.length != PROTECTOR_BYTES) {
            throw new GeneralSecurityException("protector has invalid length");
        }

        try (var input = new DataInputStream(Files.newInputStream(privateKeyFile))) {
            String magic = input.readUTF();
            int version = input.readInt();
            String keyId = input.readUTF();
            byte[] publicKey = input.readNBytes(input.readInt());
            byte[] iv = input.readNBytes(input.readInt());
            byte[] ciphertext = input.readNBytes(input.readInt());

            if (!MAGIC.equals(magic) || version != VERSION) {
                throw new GeneralSecurityException("unsupported private key container");
            }
            if (!expectedKeyId.equals(keyId)) {
                throw new GeneralSecurityException("private key container keyId mismatch");
            }
            if (iv.length != GCM_IV_BYTES || ciphertext.length == 0 || publicKey.length == 0) {
                throw new GeneralSecurityException("private key container is incomplete");
            }

            byte[] privateKeyBytes = decryptPrivateKey(keyId, protector, iv, ciphertext);
            KeyFactory keyFactory = KeyFactory.getInstance("RSA");
            PrivateKey privateKey = keyFactory
                    .generatePrivate(new PKCS8EncodedKeySpec(privateKeyBytes));
            PublicKey decodedPublicKey = keyFactory
                    .generatePublic(new X509EncodedKeySpec(publicKey));
            String publicKeyBase64 = Base64.getEncoder().encodeToString(publicKey);

            return new LoadedMasterNetworkKey(
                    NetworkIdentityCrypto.fingerprint(publicKey),
                    publicKeyBase64,
                    decodedPublicKey,
                    privateKey);
        }
    }

    private byte[] encryptPrivateKey(
            String keyId,
            byte[] protector,
            byte[] iv,
            byte[] privateKeyBytes) throws GeneralSecurityException {
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.ENCRYPT_MODE, new SecretKeySpec(protector, "AES"), new GCMParameterSpec(GCM_TAG_BITS, iv));
        cipher.updateAAD(keyId.getBytes(java.nio.charset.StandardCharsets.UTF_8));
        return cipher.doFinal(privateKeyBytes);
    }

    private byte[] decryptPrivateKey(
            String keyId,
            byte[] protector,
            byte[] iv,
            byte[] ciphertext) throws GeneralSecurityException {
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.DECRYPT_MODE, new SecretKeySpec(protector, "AES"), new GCMParameterSpec(GCM_TAG_BITS, iv));
        cipher.updateAAD(keyId.getBytes(java.nio.charset.StandardCharsets.UTF_8));
        return cipher.doFinal(ciphertext);
    }

    private void writePrivateKeyFile(
            Path path,
            String keyId,
            byte[] publicKey,
            byte[] iv,
            byte[] ciphertext) throws IOException {
        try (var fileOutput = new FileOutputStream(path.toFile());
             var output = new DataOutputStream(fileOutput)) {
            output.writeUTF(MAGIC);
            output.writeInt(VERSION);
            output.writeUTF(keyId);
            output.writeInt(publicKey.length);
            output.write(publicKey);
            output.writeInt(iv.length);
            output.write(iv);
            output.writeInt(ciphertext.length);
            output.write(ciphertext);
            output.flush();
            fileOutput.getFD().sync();
        }
    }

    private void writeBytesDurably(Path path, byte[] content) throws IOException {
        try (var output = new FileOutputStream(path.toFile())) {
            output.write(content);
            output.flush();
            output.getFD().sync();
        }
    }

    private void forceDirectory(Path directory) {
        try (FileChannel channel = FileChannel.open(directory, StandardOpenOption.READ)) {
            channel.force(true);
        } catch (IOException ignored) {
            // Some filesystems do not allow opening directories; temp files were already fsynced before the moves.
        }
    }

    private Path temporaryPath(Path target) {
        return target.resolveSibling(target.getFileName() + "." + java.util.UUID.randomUUID() + ".tmp");
    }

    private byte[] randomBytes(int length) {
        byte[] bytes = new byte[length];
        secureRandom.nextBytes(bytes);
        return bytes;
    }

    private record LoadedMasterNetworkKey(
            String publicKeyFingerprint,
            String subjectPublicKeyInfoBase64,
            PublicKey publicKey,
            PrivateKey privateKey) {
    }
}
