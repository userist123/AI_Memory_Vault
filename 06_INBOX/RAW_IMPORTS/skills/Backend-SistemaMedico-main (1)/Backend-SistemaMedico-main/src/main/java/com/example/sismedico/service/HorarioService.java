package com.example.sismedico.service;

import com.example.sismedico.dto.request.HorarioRequest;
import com.example.sismedico.entity.Horario;
import com.example.sismedico.entity.Medico;
import com.example.sismedico.repository.HorarioRepository;
import com.example.sismedico.repository.MedicoRepository;
import jakarta.transaction.Transactional;
import lombok.RequiredArgsConstructor;
import org.springframework.stereotype.Service;

import java.util.List;

@Service
@RequiredArgsConstructor
@Transactional
public class HorarioService {

    private final HorarioRepository horarioRepository;
    private final MedicoRepository medicoRepository;

    /**
     * Registrar horario
     */
    public Horario registrarHorario(HorarioRequest request) {

        Medico medico = medicoRepository.findById(request.getMedicoId())
                .orElseThrow(() ->
                        new RuntimeException("Médico no encontrado."));

        Horario horario = Horario.builder()
                .medico(medico)
                .diaSemana(request.getDiaSemana())
                .horaInicio(request.getHoraInicio())
                .horaFin(request.getHoraFin())
                .activo(request.getActivo())
                .build();

        return horarioRepository.save(horario);

    }

    /**
     * Listar horarios
     */
    @Transactional
    public List<Horario> listarHorarios() {

        return horarioRepository.findAll();

    }

    /**
     * Obtener horario por ID
     */
    @Transactional
    public Horario obtenerPorId(Long id) {

        return horarioRepository.findById(id)
                .orElseThrow(() ->
                        new RuntimeException("Horario no encontrado."));

    }

        /**
     * Listar horarios por médico
     */
    @Transactional
    public List<Horario> listarPorMedico(Long medicoId) {

        Medico medico = medicoRepository.findById(medicoId)
                .orElseThrow(() ->
                        new RuntimeException("Médico no encontrado."));

        return horarioRepository.findByMedico(medico);

    }

    /**
     * Actualizar horario
     */
    public Horario actualizarHorario(Long id, HorarioRequest request) {

        Horario horario = horarioRepository.findById(id)
                .orElseThrow(() ->
                        new RuntimeException("Horario no encontrado."));

        Medico medico = medicoRepository.findById(request.getMedicoId())
                .orElseThrow(() ->
                        new RuntimeException("Médico no encontrado."));

        horario.setMedico(medico);
        horario.setDiaSemana(request.getDiaSemana());
        horario.setHoraInicio(request.getHoraInicio());
        horario.setHoraFin(request.getHoraFin());
        horario.setActivo(request.getActivo());

        return horarioRepository.save(horario);

    }

    /**
     * Activar horario
     */
    public Horario activarHorario(Long id) {

        Horario horario = obtenerPorId(id);

        horario.setActivo(true);

        return horarioRepository.save(horario);

    }

    /**
     * Desactivar horario
     */
    public Horario desactivarHorario(Long id) {

        Horario horario = obtenerPorId(id);

        horario.setActivo(false);

        return horarioRepository.save(horario);

    }

        /**
     * Eliminar horario
     */
    public void eliminarHorario(Long id) {

        Horario horario = horarioRepository.findById(id)
                .orElseThrow(() ->
                        new RuntimeException("Horario no encontrado."));

        horarioRepository.delete(horario);

    }

    /**
     * Contar horarios registrados
     */
    @Transactional
    public Long contarHorarios() {

        return horarioRepository.count();

    }

    /**
     * Verificar si existe un horario
     */
    @Transactional
    public Boolean existeHorario(Long id) {

        return horarioRepository.existsById(id);

    }

}