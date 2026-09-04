package com.example.sismedico.controller;

import com.example.sismedico.dto.request.EspecialidadRequest;
import com.example.sismedico.entity.Especialidad;
import com.example.sismedico.service.EspecialidadService;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

import java.util.List;

@RestController
@RequestMapping("/api/especialidades")
@RequiredArgsConstructor
@CrossOrigin(origins = "*")
public class EspecialidadController {

    private final EspecialidadService especialidadService;

    /**
     * Registrar especialidad
     */
    @PostMapping
    public ResponseEntity<Especialidad> registrarEspecialidad(
            @Valid @RequestBody EspecialidadRequest request) {

        return ResponseEntity.status(HttpStatus.CREATED)
                .body(especialidadService.registrarEspecialidad(request));
    }

    /**
     * Listar especialidades
     */
    @GetMapping
    public ResponseEntity<List<Especialidad>> listarEspecialidades() {

        return ResponseEntity.ok(
                especialidadService.listarEspecialidades()
        );
    }

    /**
     * Obtener especialidad por ID
     */
    @GetMapping("/{id}")
    public ResponseEntity<Especialidad> obtenerPorId(
            @PathVariable Long id) {

        return ResponseEntity.ok(
                especialidadService.obtenerPorId(id)
        );
    }

    /**
     * Buscar especialidad por nombre
     */
    @GetMapping("/nombre/{nombre}")
    public ResponseEntity<Especialidad> buscarPorNombre(
            @PathVariable String nombre) {

        return ResponseEntity.ok(
                especialidadService.buscarPorNombre(nombre)
        );
    }

    /**
     * Actualizar especialidad
     */
    @PutMapping("/{id}")
    public ResponseEntity<Especialidad> actualizarEspecialidad(
            @PathVariable Long id,
            @Valid @RequestBody EspecialidadRequest request) {

        return ResponseEntity.ok(
                especialidadService.actualizarEspecialidad(id, request)
        );
    }

    /**
     * Activar especialidad
     */
    @PutMapping("/{id}/activar")
    public ResponseEntity<Especialidad> activarEspecialidad(
            @PathVariable Long id) {

        return ResponseEntity.ok(
                especialidadService.activarEspecialidad(id)
        );
    }

    /**
     * Desactivar especialidad
     */
    @PutMapping("/{id}/desactivar")
    public ResponseEntity<Especialidad> desactivarEspecialidad(
            @PathVariable Long id) {

        return ResponseEntity.ok(
                especialidadService.desactivarEspecialidad(id)
        );
    }

    /**
     * Eliminar especialidad
     */
    @DeleteMapping("/{id}")
    public ResponseEntity<Void> eliminarEspecialidad(
            @PathVariable Long id) {

        especialidadService.eliminarEspecialidad(id);

        return ResponseEntity.noContent().build();
    }

    /**
     * Contar especialidades
     */
    @GetMapping("/count")
    public ResponseEntity<Long> contarEspecialidades() {

        return ResponseEntity.ok(
                especialidadService.contarEspecialidades()
        );
    }

    /**
     * Verificar existencia
     */
    @GetMapping("/exists/{id}")
    public ResponseEntity<Boolean> existeEspecialidad(
            @PathVariable Long id) {

        return ResponseEntity.ok(
                especialidadService.existeEspecialidad(id)
        );
    }

}