package com.example.sismedico.mapper;

import com.example.sismedico.dto.response.CitaResponse;
import com.example.sismedico.entity.Cita;

public class CitaMapper {

    private CitaMapper() {
    }

    public static CitaResponse toResponse(Cita cita) {

        if (cita == null) {
            return null;
        }

        return CitaResponse.builder()
                .id(cita.getId())
                .uuid(cita.getUuid())

                .pacienteId(
                        cita.getPaciente() != null
                                ? cita.getPaciente().getId()
                                : null
                )

                .nombrePaciente(
                        cita.getPaciente() != null &&
                        cita.getPaciente().getUsuario() != null
                                ? cita.getPaciente().getUsuario().getNombre()
                                + " "
                                + cita.getPaciente().getUsuario().getApellido()
                                : null
                )

                .medicoId(
                        cita.getMedico() != null
                                ? cita.getMedico().getId()
                                : null
                )

                .nombreMedico(
                        cita.getMedico() != null &&
                        cita.getMedico().getUsuario() != null
                                ? cita.getMedico().getUsuario().getNombre()
                                + " "
                                + cita.getMedico().getUsuario().getApellido()
                                : null
                )

                .especialidadId(
                        cita.getEspecialidad() != null
                                ? cita.getEspecialidad().getId()
                                : null
                )

                .especialidad(
                        cita.getEspecialidad() != null
                                ? cita.getEspecialidad().getNombre()
                                : null
                )

                .fecha(cita.getFecha())
                .hora(cita.getHora())
                .motivoConsulta(cita.getMotivo())
                .observaciones(cita.getObservaciones())
                .estado(cita.getEstado())
                .tieneDiagnostico(cita.getDiagnostico() != null)
                .tieneReceta(
                        cita.getDiagnostico() != null &&
                        cita.getDiagnostico().getReceta() != null
                )
                .fechaRegistro(cita.getFechaRegistro())
                .fechaActualizacion(cita.getFechaActualizacion())
                .build();
    }

}