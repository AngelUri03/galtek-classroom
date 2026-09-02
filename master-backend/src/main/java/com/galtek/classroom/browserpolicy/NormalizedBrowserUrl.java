package com.galtek.classroom.browserpolicy;

public record NormalizedBrowserUrl(
        String canonicalUrl,
        String scheme,
        String host,
        int port,
        String path,
        String query) {
}
