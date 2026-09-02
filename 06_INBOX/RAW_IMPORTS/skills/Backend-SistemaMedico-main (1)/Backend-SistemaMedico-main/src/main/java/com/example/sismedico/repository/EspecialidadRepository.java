package com.example.sismedico.repository;

import com.example.sismedico.entity.Especialidad;
import org.springframework.data.jpa.repository.JpaRepository;

import java.util.List;
import java.util.Optional;

public interface EspecialidadRepository extends JpaRepository<Especialidad, Long> {


    /**
     * Buscar especialidad por nombre
     */
    Optional<Especialidad> findByNombre(String nombre);


    /**
     * Validar si existe una especialidad
     */
    boolean existsByNombre(String nombre);


    /**
     * Listar especialidades activas
     */
    List<Especialidad> findByActivoTrue();


    /**
     * Buscar especialidades activas por nombre
     */
    List<Especialidad> findByNombreContainingIgnoreCaseAndActivoTrue(
            String nombre
    );

}