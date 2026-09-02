package com.example.sismedico.mapper;

import com.example.sismedico.dto.response.UsuarioResponse;
import com.example.sismedico.entity.Usuario;

public class UsuarioMapper {

    private UsuarioMapper() {
    }

    public static UsuarioResponse toResponse(Usuario usuario) {

        if (usuario == null) {
            return null;
        }

        return UsuarioResponse.builder()
                .id(usuario.getId())
                .uuid(usuario.getUuid())
                .nombre(usuario.getNombre())
                .apellido(usuario.getApellido())
                .correo(usuario.getCorreo())
                .telefono(usuario.getTelefono())
                .direccion(usuario.getDireccion())
                .foto(usuario.getFoto())
                .genero(usuario.getGenero())
                .fechaNacimiento(usuario.getFechaNacimiento())

                .rolId(
                        usuario.getRol() != null
                                ? usuario.getRol().getId()
                                : null
                )

                .rol(
                        usuario.getRol() != null
                                ? usuario.getRol().getNombre().name()
                                : null
                )

                .activo(usuario.getActivo())
                .emailVerificado(usuario.getEmailVerificado())
                .tokenFirebase(usuario.getTokenFirebase())
                .ultimoAcceso(usuario.getUltimoAcceso())
                .fechaRegistro(usuario.getFechaRegistro())
                .ultimaActualizacion(usuario.getUltimaActualizacion())
                .build();
    }

}