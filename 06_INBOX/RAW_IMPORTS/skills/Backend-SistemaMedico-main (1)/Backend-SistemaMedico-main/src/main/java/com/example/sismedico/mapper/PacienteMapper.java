package com.example.sismedico.mapper;

import com.example.sismedico.dto.response.PacienteResponse;
import com.example.sismedico.entity.Paciente;

public class PacienteMapper {

    private PacienteMapper() {
    }

    public static PacienteResponse toResponse(Paciente paciente) {

        if (paciente == null) {
            return null;
        }

        return PacienteResponse.builder()
                .id(paciente.getId())

                .usuarioId(
                        paciente.getUsuario() != null
                                ? paciente.getUsuario().getId()
                                : null
                )

                .nombre(
                        paciente.getUsuario() != null
                                ? paciente.getUsuario().getNombre()
                                : null
                )

                .apellido(
                        paciente.getUsuario() != null
                                ? paciente.getUsuario().getApellido()
                                : null
                )

                .correo(
                        paciente.getUsuario() != null
                                ? paciente.getUsuario().getCorreo()
                                : null
                )

                .telefono(
                        paciente.getUsuario() != null
                                ? paciente.getUsuario().getTelefono()
                                : null
                )

                .curp(paciente.getCurp())
                .numeroSeguroSocial(paciente.getNumeroSeguroSocial())
                .fechaNacimiento(paciente.getFechaNacimiento())
                .tipoSangre(paciente.getTipoSangre())
                .altura(paciente.getAltura())
                .peso(paciente.getPeso())
                .alergias(paciente.getAlergias())
                .enfermedadesCronicas(paciente.getEnfermedadesCronicas())
                .medicamentosActuales(paciente.getMedicamentosActuales())
                .contactoEmergencia(paciente.getContactoEmergencia())
                .telefonoEmergencia(paciente.getTelefonoEmergencia())
                .activo(paciente.getActivo())
                .fechaRegistro(paciente.getFechaRegistro())
                .build();
    }

}