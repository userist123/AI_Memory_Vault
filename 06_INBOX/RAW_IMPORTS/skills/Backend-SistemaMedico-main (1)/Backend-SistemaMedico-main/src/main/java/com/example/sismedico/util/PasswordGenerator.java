package com.example.sismedico.util;

import java.security.SecureRandom;

public final class PasswordGenerator {

    private PasswordGenerator() {
        throw new IllegalStateException("Clase utilitaria");
    }

    private static final SecureRandom RANDOM = new SecureRandom();

    private static final String UPPER =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    private static final String LOWER =
            "abcdefghijklmnopqrstuvwxyz";

    private static final String NUMBERS =
            "0123456789";

    private static final String SYMBOLS =
            "!@#$%&*_-+=?";

    private static final String ALL =
            UPPER + LOWER + NUMBERS + SYMBOLS;

    /**
     * Genera una contraseña segura de 12 caracteres.
     */
    public static String generate() {

        return generate(12);

    }

    /**
     * Genera una contraseña con la longitud indicada.
     */
    public static String generate(int length) {

        if (length < 8) {
            throw new IllegalArgumentException(
                    "La longitud mínima es de 8 caracteres."
            );
        }

        StringBuilder password = new StringBuilder();

        // Garantiza al menos un carácter de cada tipo
        password.append(randomChar(UPPER));
        password.append(randomChar(LOWER));
        password.append(randomChar(NUMBERS));
        password.append(randomChar(SYMBOLS));

        // Completa el resto
        for (int i = password.length(); i < length; i++) {
            password.append(randomChar(ALL));
        }

        return shuffle(password.toString());

    }

    /**
     * Obtiene un carácter aleatorio.
     */
    private static char randomChar(String source) {

        return source.charAt(
                RANDOM.nextInt(source.length())
        );

    }

    /**
     * Mezcla los caracteres de la contraseña.
     */
    private static String shuffle(String value) {

        char[] chars = value.toCharArray();

        for (int i = chars.length - 1; i > 0; i--) {

            int j = RANDOM.nextInt(i + 1);

            char temp = chars[i];
            chars[i] = chars[j];
            chars[j] = temp;

        }

        return new String(chars);

    }

    /**
     * Valida si una contraseña cumple con los requisitos mínimos.
     */
    public static boolean isStrong(String password) {

        if (password == null || password.length() < 8) {
            return false;
        }

        boolean upper = false;
        boolean lower = false;
        boolean number = false;
        boolean symbol = false;

        for (char c : password.toCharArray()) {

            if (Character.isUpperCase(c)) {
                upper = true;
            } else if (Character.isLowerCase(c)) {
                lower = true;
            } else if (Character.isDigit(c)) {
                number = true;
            } else {
                symbol = true;
            }

        }

        return upper && lower && number && symbol;

    }

}