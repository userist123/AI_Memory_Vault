package com.example.sismedico.service;

import com.example.sismedico.dto.request.CancelarCitaRequest;
import com.example.sismedico.dto.request.CitaRequest;
import com.example.sismedico.dto.request.ReprogramarCitaRequest;
import com.example.sismedico.dto.response.CitaResponse;
import com.example.sismedico.entity.Cita;
import com.example.sismedico.entity.Especialidad;
import com.example.sismedico.entity.Medico;
import com.example.sismedico.entity.Paciente;
import com.example.sismedico.enums.EstadoCita;
import com.example.sismedico.repository.CitaRepository;
import com.example.sismedico.repository.EspecialidadRepository;
import com.example.sismedico.repository.MedicoRepository;
import com.example.sismedico.repository.PacienteRepository;
import lombok.RequiredArgsConstructor;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.util.List;

@Service
@RequiredArgsConstructor
@Transactional
public class CitaService {


    private final CitaRepository citaRepository;

    private final PacienteRepository pacienteRepository;

    private final MedicoRepository medicoRepository;

    private final EspecialidadRepository especialidadRepository;


    /**
     * Crear una nueva cita
     */
    public CitaResponse crearCita(CitaRequest request) {


        Paciente paciente = pacienteRepository.findById(request.getPacienteId())
                .orElseThrow(() ->
                        new RuntimeException("Paciente no encontrado"));


        Medico medico = medicoRepository.findById(request.getMedicoId())
                .orElseThrow(() ->
                        new RuntimeException("Médico no encontrado"));


        Especialidad especialidad =
                especialidadRepository.findById(request.getEspecialidadId())
                        .orElseThrow(() ->
                                new RuntimeException(
                                        "Especialidad no encontrada"));


        if (!paciente.getActivo()) {
            throw new RuntimeException(
                    "El paciente está inactivo");
        }


        if (!medico.getActivo()) {
            throw new RuntimeException(
                    "El médico está inactivo");
        }


        Cita cita = Cita.builder()
                .paciente(paciente)
                .medico(medico)
                .especialidad(especialidad)
                .fecha(request.getFecha())
                .hora(request.getHora())
                .motivo(request.getMotivo())
                .observaciones(request.getObservaciones())
                .estado(EstadoCita.PENDIENTE)
                .build();


        Cita guardada = citaRepository.save(cita);


        return convertirResponse(guardada);

    }



    /**
     * Obtener todas las citas
     */
    @Transactional(readOnly = true)
    public List<CitaResponse> listarCitas() {


        return citaRepository.findAll()
                .stream()
                .map(this::convertirResponse)
                .toList();

    }



    /**
     * Obtener cita por ID
     */
    @Transactional(readOnly = true)
    public CitaResponse obtenerPorId(Long id) {


        Cita cita = citaRepository.findById(id)
                .orElseThrow(() ->
                        new RuntimeException(
                                "Cita no encontrada"));


        return convertirResponse(cita);

    }



    /**
     * Actualizar cita
     */
    public CitaResponse actualizarCita(
            Long id,
            CitaRequest request) {


        Cita cita = citaRepository.findById(id)
                .orElseThrow(() ->
                        new RuntimeException(
                                "Cita no encontrada"));



        Paciente paciente = pacienteRepository.findById(
                        request.getPacienteId())
                .orElseThrow(() ->
                        new RuntimeException(
                                "Paciente no encontrado"));



        Medico medico = medicoRepository.findById(
                        request.getMedicoId())
                .orElseThrow(() ->
                        new RuntimeException(
                                "Médico no encontrado"));



        Especialidad especialidad =
                especialidadRepository.findById(
                                request.getEspecialidadId())
                        .orElseThrow(() ->
                                new RuntimeException(
                                        "Especialidad no encontrada"));



        cita.setPaciente(paciente);

        cita.setMedico(medico);

        cita.setEspecialidad(especialidad);

        cita.setFecha(request.getFecha());

        cita.setHora(request.getHora());

        cita.setMotivo(request.getMotivo());

        cita.setObservaciones(request.getObservaciones());



        return convertirResponse(
                citaRepository.save(cita)
        );

    }

        /**
     * Cancelar una cita
     */
    public void cancelarCita(
            Long id,
            CancelarCitaRequest request) {


        Cita cita = citaRepository.findById(id)
                .orElseThrow(() ->
                        new RuntimeException(
                                "Cita no encontrada"));


        if (cita.getEstado() == EstadoCita.CANCELADA) {

            throw new RuntimeException(
                    "La cita ya está cancelada");

        }


        cita.setEstado(EstadoCita.CANCELADA);


        if (request.getMotivo() != null &&
                !request.getMotivo().isBlank()) {

            cita.setObservaciones(
                    cita.getObservaciones()
                            + "\nMotivo cancelación: "
                            + request.getMotivo()
            );

        }


        citaRepository.save(cita);

    }



    /**
     * Reprogramar una cita
     */
    public CitaResponse reprogramarCita(
            Long id,
            ReprogramarCitaRequest request) {


        Cita cita = citaRepository.findById(id)
                .orElseThrow(() ->
                        new RuntimeException(
                                "Cita no encontrada"));



        if (cita.getEstado() == EstadoCita.CANCELADA) {

            throw new RuntimeException(
                    "No se puede reprogramar una cita cancelada");

        }



        cita.setFecha(request.getNuevaFecha());

        cita.setHora(request.getNuevaHora());

        cita.setEstado(EstadoCita.PENDIENTE);



        return convertirResponse(
                citaRepository.save(cita)
        );

    }



    /**
     * Eliminar una cita
     */
    public void eliminarCita(Long id) {


        Cita cita = citaRepository.findById(id)
                .orElseThrow(() ->
                        new RuntimeException(
                                "Cita no encontrada"));



        citaRepository.delete(cita);

    }



    /**
     * Buscar citas por paciente
     */
    @Transactional(readOnly = true)
    public List<CitaResponse> listarPorPaciente(
            Long pacienteId) {


        Paciente paciente = pacienteRepository.findById(
                        pacienteId)
                .orElseThrow(() ->
                        new RuntimeException(
                                "Paciente no encontrado"));



        return citaRepository.findByPaciente(paciente)
                .stream()
                .map(this::convertirResponse)
                .toList();

    }



    /**
     * Buscar citas por médico
     */
    @Transactional(readOnly = true)
    public List<CitaResponse> listarPorMedico(
            Long medicoId) {


        Medico medico = medicoRepository.findById(
                        medicoId)
                .orElseThrow(() ->
                        new RuntimeException(
                                "Médico no encontrado"));



        return citaRepository.findByMedico(medico)
                .stream()
                .map(this::convertirResponse)
                .toList();

    }



    /**
     * Buscar citas por estado
     */
    @Transactional(readOnly = true)
    public List<CitaResponse> listarPorEstado(
            EstadoCita estado) {


        return citaRepository.findByEstado(estado)
                .stream()
                .map(this::convertirResponse)
                .toList();

    }

        /**
     * Convertir entidad Cita a CitaResponse
     */
    private CitaResponse convertirResponse(Cita cita) {


        return CitaResponse.builder()

                .id(cita.getId())

                .uuid(cita.getUuid())


                // Paciente
                .pacienteId(
                        cita.getPaciente().getId()
                )

                .nombrePaciente(
                        cita.getPaciente()
                                .getUsuario()
                                .getNombre()
                        + " "
                        +
                        cita.getPaciente()
                                .getUsuario()
                                .getApellido()
                )


                // Médico
                .medicoId(
                        cita.getMedico().getId()
                )

                .nombreMedico(
                        cita.getMedico()
                                .getUsuario()
                                .getNombre()
                        + " "
                        +
                        cita.getMedico()
                                .getUsuario()
                                .getApellido()
                )


                // Especialidad
                .especialidadId(
                        cita.getEspecialidad().getId()
                )

                .especialidad(
                        cita.getEspecialidad()
                                .getNombre()
                )


                // Fecha y hora
                .fecha(
                        cita.getFecha()
                )

                .hora(
                        cita.getHora()
                )


                // Información de cita
                .motivoConsulta(
                        cita.getMotivo()
                )

                .observaciones(
                        cita.getObservaciones()
                )


                // Estado
                .estado(
                        cita.getEstado()
                )


                // Diagnóstico
                .tieneDiagnostico(
                        cita.getDiagnostico() != null
                )


                // Receta todavía no relacionada
                .tieneReceta(false)


                // Auditoría
                .fechaRegistro(
                        cita.getFechaRegistro()
                )

                .fechaActualizacion(
                        cita.getFechaActualizacion()
                )


                .build();

    }



    /**
     * Validar disponibilidad de horario
     */
    private void validarHorarioDisponible(
            Medico medico,
            java.time.LocalDate fecha,
            java.time.LocalTime hora) {


        List<Cita> citas =
                citaRepository.findByMedico(medico);



        boolean ocupado = citas.stream()
                .anyMatch(cita ->
                        cita.getFecha().equals(fecha)
                        &&
                        cita.getHora().equals(hora)
                        &&
                        cita.getEstado()
                                != EstadoCita.CANCELADA
                );



        if (ocupado) {

            throw new RuntimeException(
                    "El médico ya tiene una cita en ese horario"
            );

        }

    }



    /**
     * Verificar que una cita pueda modificarse
     */
    private void validarEstadoCita(Cita cita) {


        if (cita.getEstado()
                == EstadoCita.CANCELADA) {


            throw new RuntimeException(
                    "La cita está cancelada y no puede modificarse"
            );

        }

    }

}