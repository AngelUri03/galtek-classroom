package com.galtek.classroom.persistence.sqlite;

import com.galtek.classroom.persistence.PersistenceVersionConflictException;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.util.Optional;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.jdbc.core.RowMapper;

public final class SqliteJdbc {

    private SqliteJdbc() {
    }

    public static <T> Optional<T> optional(
            JdbcTemplate jdbcTemplate,
            String sql,
            RowMapper<T> rowMapper,
            Object... args) {
        return jdbcTemplate.query(sql, rowMapper, args).stream().findFirst();
    }

    public static int bool(boolean value) {
        return value ? 1 : 0;
    }

    public static boolean bool(ResultSet resultSet, String column) throws SQLException {
        return resultSet.getInt(column) == 1;
    }

    public static void requireUpdated(int updatedRows, String message) {
        if (updatedRows == 0) {
            throw new PersistenceVersionConflictException(message);
        }
    }
}
