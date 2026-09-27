package com.yilkgames.nuggetcreek;

import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;

import java.security.Key;
import java.security.KeyStore;

import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.spec.GCMParameterSpec;

/**
 * Wraps the save signing key with an AES-GCM key that never leaves AndroidKeyStore
 * (design doc 13.1 rule 4). Called from SaveKey.cs; the wrapped form is "base64 iv:base64 data".
 */
public final class SaveKeystore {
    private static final String PROVIDER = "AndroidKeyStore";
    private static final String TRANSFORMATION = "AES/GCM/NoPadding";
    private static final int TAG_BITS = 128;

    private SaveKeystore() {
    }

    public static String wrap(String alias, byte[] plain) throws Exception {
        Cipher cipher = Cipher.getInstance(TRANSFORMATION);
        cipher.init(Cipher.ENCRYPT_MODE, keyFor(alias));
        byte[] sealed = cipher.doFinal(plain);
        return Base64.encodeToString(cipher.getIV(), Base64.NO_WRAP) + ":"
                + Base64.encodeToString(sealed, Base64.NO_WRAP);
    }

    public static byte[] unwrap(String alias, String wrapped) throws Exception {
        int split = wrapped.indexOf(':');
        if (split < 0)
            throw new IllegalArgumentException("Wrapped key is malformed.");
        byte[] iv = Base64.decode(wrapped.substring(0, split), Base64.NO_WRAP);
        byte[] sealed = Base64.decode(wrapped.substring(split + 1), Base64.NO_WRAP);
        Cipher cipher = Cipher.getInstance(TRANSFORMATION);
        cipher.init(Cipher.DECRYPT_MODE, keyFor(alias), new GCMParameterSpec(TAG_BITS, iv));
        return cipher.doFinal(sealed);
    }

    private static Key keyFor(String alias) throws Exception {
        KeyStore store = KeyStore.getInstance(PROVIDER);
        store.load(null);
        if (!store.containsAlias(alias)) {
            KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, PROVIDER);
            generator.init(new KeyGenParameterSpec.Builder(alias,
                    KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                    .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                    .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                    .setKeySize(256)
                    .build());
            return generator.generateKey();
        }
        return store.getKey(alias, null);
    }
}
