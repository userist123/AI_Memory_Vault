package com.example.sismedico.util;

public final class Constants {

    private Constants() {
        throw new IllegalStateException("Clase de utilidades");
    }

    // ==========================================
    // API
    // ==========================================

    public static final String API_BASE = "/api";
    public static final String API_VERSION = "/v1";

    // ==========================================
    // AUTENTICACIÓN
    // ==========================================

    public static final String TOKEN_PREFIX = "Bearer ";
    public static final String HEADER_AUTHORIZATION = "Authorization";

    // ==========================================
    // MENSAJES GENERALES
    // ==========================================

    public static final String REGISTRO_EXITOSO = "Registro realizado correctamente.";
    public static final String ACTUALIZACION_EXITOSA = "Registro actualizado correctamente.";
    public static final String ELIMINACION_EXITOSA = "Registro eliminado correctamente.";

    // ==========================================
    // MENSAJES DE ERROR
    // ==========================================

    public static final String ERROR_INTERNO = "Ha ocurrido un error interno del servidor.";

    public static final String USUARIO_NO_ENCONTRADO = "Usuario no encontrado.";
    public static final String PACIENTE_NO_ENCONTRADO = "Paciente no encontrado.";
    public static final String MEDICO_NO_ENCONTRADO = "Médico no encontrado.";
    public static final String CITA_NO_ENCONTRADA = "Cita no encontrada.";
    public static final String DIAGNOSTICO_NO_ENCONTRADO = "Diagnóstico no encontrado.";
    public static final String RECETA_NO_ENCONTRADA = "Receta no encontrada.";
    public static final String HORARIO_NO_ENCONTRADO = "Horario no encontrado.";
    public static final String ESPECIALIDAD_NO_ENCONTRADA = "Especialidad no encontrada.";
    public static final String NOTIFICACION_NO_ENCONTRADA = "Notificación no encontrada.";

    // ==========================================
    // LOGIN
    // ==========================================

    public static final String LOGIN_EXITOSO = "Inicio de sesión exitoso.";
    public static final String CREDENCIALES_INVALIDAS = "Correo o contraseña incorrectos.";
    public static final String TOKEN_INVALIDO = "Token inválido.";
    public static final String TOKEN_EXPIRADO = "El token ha expirado.";

    // ==========================================
    // VALIDACIONES
    // ==========================================

    public static final String CORREO_YA_EXISTE = "El correo ya está registrado.";
    public static final String CURP_YA_EXISTE = "La CURP ya está registrada.";
    public static final String CEDULA_YA_EXISTE = "La cédula profesional ya está registrada.";
    public static final String PASSWORDS_NO_COINCIDEN = "Las contraseñas no coinciden.";

    // ==========================================
    // ESTADOS
    // ==========================================

    public static final Boolean ACTIVO = true;
    public static final Boolean INACTIVO = false;

}