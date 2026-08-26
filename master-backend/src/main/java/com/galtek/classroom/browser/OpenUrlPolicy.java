package com.galtek.classroom.browser;

import java.net.URI;
import java.net.URISyntaxException;
import java.util.Set;

public final class OpenUrlPolicy {

    private static final Set<String> ALLOWED_SCHEMES = Set.of("http", "https");

    public UrlValidationResult validate(String url) {
        if (url == null || url.isBlank()) {
            return UrlValidationResult.invalid();
        }

        try {
            var uri = new URI(url);
            var scheme = uri.getScheme();
            if (scheme == null || !ALLOWED_SCHEMES.contains(scheme.toLowerCase())) {
                return UrlValidationResult.invalid();
            }

            if (uri.getHost() == null || uri.getHost().isBlank()) {
                return UrlValidationResult.invalid();
            }

            return UrlValidationResult.allowed();
        } catch (URISyntaxException exception) {
            return UrlValidationResult.invalid();
        }
    }
}
