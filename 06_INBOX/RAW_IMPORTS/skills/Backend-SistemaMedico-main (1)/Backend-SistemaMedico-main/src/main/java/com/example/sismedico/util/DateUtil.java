package com.example.sismedico.util;

import java.time.LocalDate;
import java.time.LocalDateTime;
import java.time.LocalTime;
import java.time.Period;
import java.time.format.DateTimeFormatter;

public final class DateUtil {

    private DateUtil() {
        throw new IllegalStateException("Clase utilitaria");
    }

    // ==========================
    // FORMATOS
    // ==========================

    public static final String DATE_PATTERN = "dd/MM/yyyy";
    public static final String TIME_PATTERN = "HH:mm";
    public static final String DATE_TIME_PATTERN = "dd/MM/yyyy HH:mm";

    private static final DateTimeFormatter DATE_FORMATTER =
            DateTimeFormatter.ofPattern(DATE_PATTERN);

    private static final DateTimeFormatter TIME_FORMATTER =
            DateTimeFormatter.ofPattern(TIME_PATTERN);

    private static final DateTimeFormatter DATE_TIME_FORMATTER =
            DateTimeFormatter.ofPattern(DATE_TIME_PATTERN);

    // ==========================
    // FORMATEAR
    // ==========================

    public static String format(LocalDate date) {

        if (date == null) {
            return null;
        }

        return date.format(DATE_FORMATTER);
    }

    public static String format(LocalTime time) {

        if (time == null) {
            return null;
        }

        return time.format(TIME_FORMATTER);
    }

    public static String format(LocalDateTime dateTime) {

        if (dateTime == null) {
            return null;
        }

        return dateTime.format(DATE_TIME_FORMATTER);
    }

    // ==========================
    // PARSE
    // ==========================

    public static LocalDate parseDate(String date) {

        return LocalDate.parse(date, DATE_FORMATTER);

    }

    public static LocalTime parseTime(String time) {

        return LocalTime.parse(time, TIME_FORMATTER);

    }

    public static LocalDateTime parseDateTime(String dateTime) {

        return LocalDateTime.parse(dateTime, DATE_TIME_FORMATTER);

    }

    // ==========================
    // FECHAS ACTUALES
    // ==========================

    public static LocalDate today() {

        return LocalDate.now();

    }

    public static LocalTime nowTime() {

        return LocalTime.now();

    }

    public static LocalDateTime now() {

        return LocalDateTime.now();

    }

    // ==========================
    // VALIDACIONES
    // ==========================

    public static boolean isToday(LocalDate date) {

        return LocalDate.now().equals(date);

    }

    public static boolean isFuture(LocalDate date) {

        return date != null && date.isAfter(LocalDate.now());

    }

    public static boolean isPast(LocalDate date) {

        return date != null && date.isBefore(LocalDate.now());

    }

    // ==========================
    // EDAD
    // ==========================

    public static int calcularEdad(LocalDate fechaNacimiento) {

        if (fechaNacimiento == null) {
            return 0;
        }

        return Period.between(fechaNacimiento, LocalDate.now()).getYears();

    }

    // ==========================
    // DIFERENCIAS
    // ==========================

    public static long diasEntre(LocalDate inicio, LocalDate fin) {

        return java.time.temporal.ChronoUnit.DAYS.between(inicio, fin);

    }

    public static long horasEntre(LocalDateTime inicio, LocalDateTime fin) {

        return java.time.temporal.ChronoUnit.HOURS.between(inicio, fin);

    }

}