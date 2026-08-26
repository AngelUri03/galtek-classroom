package com.galtek.classroom.browser;

import static org.assertj.core.api.Assertions.assertThat;

import com.galtek.classroom.operations.ErrorCode;
import org.junit.jupiter.api.Test;

class OpenUrlPolicyTest {

    private final OpenUrlPolicy policy = new OpenUrlPolicy();

    @Test
    void httpAndHttpsUrlsAreValid() {
        assertThat(policy.validate("https://example.edu/recurso").valid()).isTrue();
        assertThat(policy.validate("http://example.edu/recurso").valid()).isTrue();
    }

    @Test
    void unsafeSchemesAreRejected() {
        assertThat(policy.validate("file:///C:/Windows/System32/calc.exe").errorCode())
                .isEqualTo(ErrorCode.INVALID_URL);
        assertThat(policy.validate("javascript:alert(1)").errorCode())
                .isEqualTo(ErrorCode.INVALID_URL);
        assertThat(policy.validate("data:text/html;base64,PGgxPk5vPC9oMT4=").errorCode())
                .isEqualTo(ErrorCode.INVALID_URL);
    }
}
