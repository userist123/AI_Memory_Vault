package com.example.sismedico.util;

import org.springframework.web.multipart.MultipartFile;

import java.io.IOException;
import java.nio.file.*;
import java.util.UUID;

public final class FileUtil {

    private FileUtil() {
        throw new IllegalStateException("Clase utilitaria");
    }

    /**
     * Obtiene la extensión de un archivo.
     */
    public static String getExtension(String fileName) {

        if (fileName == null || !fileName.contains(".")) {
            return "";
        }

        return fileName.substring(fileName.lastIndexOf(".") + 1)
                .toLowerCase();

    }

    /**
     * Obtiene el nombre del archivo sin extensión.
     */
    public static String getFileNameWithoutExtension(String fileName) {

        if (fileName == null || !fileName.contains(".")) {
            return fileName;
        }

        return fileName.substring(0, fileName.lastIndexOf("."));

    }

    /**
     * Genera un nombre único conservando la extensión.
     */
    public static String generateUniqueFileName(String originalFileName) {

        String extension = getExtension(originalFileName);

        if (extension.isEmpty()) {
            return UUID.randomUUID().toString();
        }

        return UUID.randomUUID() + "." + extension;

    }

    /**
     * Valida que el archivo no esté vacío.
     */
    public static boolean isValid(MultipartFile file) {

        return file != null &&
                !file.isEmpty() &&
                file.getOriginalFilename() != null;

    }

    /**
     * Guarda un archivo.
     */
    public static String saveFile(
            MultipartFile file,
            String uploadDir
    ) throws IOException {

        if (!isValid(file)) {
            throw new IOException("Archivo inválido.");
        }

        Path uploadPath = Paths.get(uploadDir);

        if (!Files.exists(uploadPath)) {
            Files.createDirectories(uploadPath);
        }

        String fileName = generateUniqueFileName(file.getOriginalFilename());

        Path destination = uploadPath.resolve(fileName);

        Files.copy(
                file.getInputStream(),
                destination,
                StandardCopyOption.REPLACE_EXISTING
        );

        return fileName;

    }

    /**
     * Elimina un archivo.
     */
    public static boolean deleteFile(
            String uploadDir,
            String fileName
    ) throws IOException {

        Path file = Paths.get(uploadDir).resolve(fileName);

        return Files.deleteIfExists(file);

    }

    /**
     * Verifica si existe un archivo.
     */
    public static boolean exists(
            String uploadDir,
            String fileName
    ) {

        return Files.exists(
                Paths.get(uploadDir).resolve(fileName)
        );

    }

    /**
     * Obtiene el tamaño del archivo en bytes.
     */
    public static long size(MultipartFile file) {

        return file.getSize();

    }

    /**
     * Obtiene el Content-Type.
     */
    public static String contentType(MultipartFile file) {

        return file.getContentType();

    }

    /**
     * Verifica si es una imagen.
     */
    public static boolean isImage(MultipartFile file) {

        if (file == null || file.getContentType() == null) {
            return false;
        }

        return file.getContentType().startsWith("image/");

    }

}