package com.example.sismedico.mapper;

import com.example.sismedico.dto.response.MedicoResponse;
import com.example.sismedico.entity.Medico;

public class MedicoMapper {

    private MedicoMapper() {
    }

    public static MedicoResponse toResponse(Medico medico) {

        if (medico == null) {
            return null;
        }

        return MedicoResponse.builder()
                .id(medico.getId())

                .usuarioId(
                        medico.getUsuario() != null
                                ? medico.getUsuario().getId()
                                : null
                )

                .nombre(
                        medico.getUsuario() != null
                                ? medico.getUsuario().getNombre()
                                : null
                )

                .apellido(
                        medico.getUsuario() != null
                                ? medico.getUsuario().getApellido()
                                : null
                )

                .correo(
                        medico.getUsuario() != null
                                ? medico.getUsuario().getCorreo()
                                : null
                )

                .telefono(
                        medico.getUsuario() != null
                                ? medico.getUsuario().getTelefono()
                                : null
                )

                .especialidadId(
                        medico.getEspecialidad() != null
                                ? medico.getEspecialidad().getId()
                                : null
                )

                .especialidad(
                        medico.getEspecialidad() != null
                                ? medico.getEspecialidad().getNombre()
                                : null
                )

                .cedulaProfesional(medico.getCedulaProfesional())
                .biografia(medico.getBiografia())
                .experiencia(medico.getExperiencia())
                .costoConsulta(medico.getCostoConsulta())
                .consultorio(medico.getConsultorio())
                .activo(medico.getActivo())
                .fechaRegistro(medico.getFechaRegistro())
                .build();
    }

}