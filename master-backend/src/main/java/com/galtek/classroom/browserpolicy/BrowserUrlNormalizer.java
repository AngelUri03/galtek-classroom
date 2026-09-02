package com.galtek.classroom.browserpolicy;

import java.net.URI;
import java.net.URISyntaxException;
import java.util.Locale;
import java.util.Optional;
import java.util.Set;

public final class BrowserUrlNormalizer {

    public static final int MAX_URL_LENGTH = 4096;

    private static final Set<String> SAFE_SCHEMES = Set.of("http", "https");

    public Optional<NormalizedBrowserUrl> normalize(String rawUrl) {
        if (rawUrl == null || rawUrl.isBlank() || rawUrl.length() > MAX_URL_LENGTH || hasControl(rawUrl)) {
            return Optional.empty();
        }

        try {
            URI parsed = new URI(rawUrl.trim());
            String scheme = lower(parsed.getScheme());
            if (scheme == null || !SAFE_SCHEMES.contains(scheme) || !parsed.isAbsolute()) {
                return Optional.empty();
            }
            if (parsed.getUserInfo() != null) {
                return Optional.empty();
            }

            String host = normalizeHost(parsed.getHost());
            if (host == null) {
                return Optional.empty();
            }

            int port = normalizePort(scheme, parsed.getPort());
            String path = parsed.getRawPath();
            if (path == null || path.isBlank()) {
                path = "/";
            }

            URI normalized = new URI(
                    scheme,
                    null,
                    host,
                    port,
                    path,
                    parsed.getRawQuery(),
                    null).normalize();
            String normalizedPath = normalized.getRawPath();
            if (normalizedPath == null || normalizedPath.isBlank()) {
                normalizedPath = "/";
                normalized = new URI(
                        scheme,
                        null,
                        host,
                        port,
                        normalizedPath,
                        parsed.getRawQuery(),
                        null);
            }

            return Optional.of(new NormalizedBrowserUrl(
                    normalized.toASCIIString(),
                    scheme,
                    host,
                    port,
                    normalizedPath,
                    normalized.getRawQuery()));
        } catch (URISyntaxException | IllegalArgumentException exception) {
            return Optional.empty();
        }
    }

    public Optional<String> normalizeHostPattern(String pattern) {
        if (pattern == null || pattern.isBlank() || hasControl(pattern)) {
            return Optional.empty();
        }

        String clean = pattern.trim();
        if (clean.contains("://")
                || clean.contains("/")
                || clean.contains("?")
                || clean.contains("#")
                || clean.contains("@")) {
            return Optional.empty();
        }

        if (clean.startsWith("[") && clean.endsWith("]")) {
            try {
                URI uri = new URI("http://" + clean + "/");
                return Optional.ofNullable(normalizeHost(uri.getHost()));
            } catch (URISyntaxException exception) {
                return Optional.empty();
            }
        }

        if (clean.contains(":")) {
            return Optional.empty();
        }

        return Optional.ofNullable(normalizeHost(clean));
    }

    public Optional<String> normalizeUrlRulePattern(BrowserUrlMatchType matchType, String pattern) {
        return switch (matchType) {
            case HOST_EXACT, HOST_SUFFIX -> normalizeHostPattern(pattern);
            case EXACT_URL -> normalize(pattern).map(NormalizedBrowserUrl::canonicalUrl);
            case URL_PREFIX -> normalize(pattern)
                    .filter(url -> url.host() != null)
                    .filter(url -> url.query() == null)
                    .map(NormalizedBrowserUrl::canonicalUrl);
        };
    }

    private int normalizePort(String scheme, int port) {
        if (("http".equals(scheme) && port == 80) || ("https".equals(scheme) && port == 443)) {
            return -1;
        }
        return port;
    }

    private String normalizeHost(String host) {
        if (host == null || host.isBlank() || hasControl(host)) {
            return null;
        }

        String clean = host.trim().toLowerCase(Locale.ROOT);
        while (clean.endsWith(".")) {
            clean = clean.substring(0, clean.length() - 1);
        }
        if (clean.isBlank() || clean.startsWith(".") || clean.contains("..")) {
            return null;
        }
        return clean;
    }

    private String lower(String value) {
        return value == null ? null : value.toLowerCase(Locale.ROOT);
    }

    private boolean hasControl(String value) {
        for (int index = 0; index < value.length(); index++) {
            if (Character.isISOControl(value.charAt(index))) {
                return true;
            }
        }
        return false;
    }
}
