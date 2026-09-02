package com.galtek.classroom.browser;

import com.galtek.classroom.browserpolicy.BrowserUrlNormalizer;

public final class OpenUrlPolicy {

    private final BrowserUrlNormalizer normalizer = new BrowserUrlNormalizer();

    public UrlValidationResult validate(String url) {
        return normalizer.normalize(url).isPresent()
                ? UrlValidationResult.allowed()
                : UrlValidationResult.invalid();
    }
}
