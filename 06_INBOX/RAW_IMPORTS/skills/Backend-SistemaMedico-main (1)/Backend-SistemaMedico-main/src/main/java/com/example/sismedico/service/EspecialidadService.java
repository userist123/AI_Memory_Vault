package com.example.sismedico.service;

import com.example.sismedico.dto.request.EspecialidadRequest;
import com.example.sismedico.entity.Especialidad;
import com.example.sismedico.repository.EspecialidadRepository;
import jakarta.transaction.Transactional;
import lombok.RequiredArgsConstructor;
import org.springframework.stereotype.Service;

import java.util.List;

@Service
@RequiredArgsConstructor
@Transactional
public class EspecialidadService {

    private final EspecialidadRepository especialidadRepository;

    /**
     * Registrar especialidad
     */
    public Especialidad registrarEspecialidad(EspecialidadRequest request) {

        if (especialidadRepository.existsByNombre(request.getNombre())) {
            throw new RuntimeException("Ya existe una especialidad con ese nombre.");
        }

        Especialidad especialidad = Especialidad.builder()
                .nombre(request.getNombre())
                .descripcion(request.getDescripcion())
                .icono(request.getIcono())
                .color(request.getColor())
                .ubicacion(request.getUbicacion())
                .duracionConsulta(request.getDuracionConsulta())
                .costoConsulta(request.getCostoConsulta())
                .activo(request.getActivo())
                .build();

        return especialidadRepository.save(especialidad);
    }

    /**
     * Obtener todas las especialidades
     */
    @Transactional
    public List<Especialidad> listarEspecialidades() {

        return especialidadRepository.findAll();

    }

    /**
     * Obtener especialidad por ID
     */
    @Transactional
    public Especialidad obtenerPorId(Long id) {

        return especialidadRepository.findById(id)
                .orElseThrow(() ->
                        new RuntimeException("Especialidad no encontrada."));

    }

    /**
     * Buscar especialidad por nombre
     */
    @Transactional
    public Especialidad buscarPorNombre(String nombre) {

        return especialidadRepository.findByNombre(nombre)
                .orElseThrow(() ->
                        new RuntimeException("Especialidad no encontrada."));

    }

        /**
     * Actualizar especialidad
     */
    public Especialidad actualizarEspecialidad(Long id, EspecialidadRequest request) {

        Especialidad especialidad = especialidadRepository.findById(id)
                .orElseThrow(() ->
                        new RuntimeException("Especialidad no encontrada."));

        if (!especialidad.getNombre().equalsIgnoreCase(request.getNombre())
                && especialidadRepository.existsByNombre(request.getNombre())) {

            throw new RuntimeException("Ya existe una especialidad con ese nombre.");

        }

        especialidad.setNombre(request.getNombre());
        especialidad.setDescripcion(request.getDescripcion());
        especialidad.setIcono(request.getIcono());
        especialidad.setColor(request.getColor());
        especialidad.setUbicacion(request.getUbicacion());
        especialidad.setDuracionConsulta(request.getDuracionConsulta());
        especialidad.setCostoConsulta(request.getCostoConsulta());
        especialidad.setActivo(request.getActivo());

        return especialidadRepository.save(especialidad);

    }

    /**
     * Activar especialidad
     */
    public Especialidad activarEspecialidad(Long id) {

        Especialidad especialidad = obtenerPorId(id);

        especialidad.setActivo(true);

        return especialidadRepository.save(especialidad);

    }

    /**
     * Desactivar especialidad
     */
    public Especialidad desactivarEspecialidad(Long id) {

        Especialidad especialidad = obtenerPorId(id);

        especialidad.setActivo(false);

        return especialidadRepository.save(especialidad);

    }

    /**
     * Eliminar especialidad
     */
    public void eliminarEspecialidad(Long id) {

        Especialidad especialidad = obtenerPorId(id);

        especialidadRepository.delete(especialidad);

    }

        /**
     * Contar especialidades
     */
    @Transactional
    public Long contarEspecialidades() {

        return especialidadRepository.count();

    }

    /**
     * Verificar si existe una especialidad
     */
    @Transactional
    public Boolean existeEspecialidad(Long id) {

        return especialidadRepository.existsById(id);

    }

}