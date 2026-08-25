package com.galtek.classroom;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;

@SpringBootApplication
public class MasterBackendApplication {

    public static void main(String[] args) {
        if (System.getProperty("debug") == null) {
            System.setProperty("debug", "false");
        }

        SpringApplication.run(MasterBackendApplication.class, args);
    }
}
