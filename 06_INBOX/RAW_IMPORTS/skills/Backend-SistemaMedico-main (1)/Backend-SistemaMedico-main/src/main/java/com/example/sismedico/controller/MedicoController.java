package com.example.sismedico.controller;

import com.example.sismedico.dto.request.MedicoRequest;
import com.example.sismedico.entity.Medico;
import com.example.sismedico.service.MedicoService;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

import java.util.List;

@RestController
@RequestMapping("/api/medicos")
@RequiredArgsConstructor
@CrossOrigin(origins = "*")
public class MedicoController {

    private final MedicoService medicoService;

    /**
     * Registrar médico
     */
    @PostMapping
    public ResponseEntity<Medico> registrarMedico(
            @Valid @RequestBody MedicoRequest request) {

        return ResponseEntity.status(HttpStatus.CREATED)
                .body(medicoService.registrarMedico(request));
    }

    /**
     * Listar médicos
     */
    @GetMapping
    public ResponseEntity<List<Medico>> listarMedicos() {

        return ResponseEntity.ok(
                medicoService.listarMedicos()
        );
    }

    /**
     * Obtener médico por ID
     */
    @GetMapping("/{id}")
    public ResponseEntity<Medico> obtenerPorId(
            @PathVariable Long id) {

        return ResponseEntity.ok(
                medicoService.obtenerPorId(id)
        );
    }

    /**
     * Listar médicos activos
     */
    @GetMapping("/activos")
    public ResponseEntity<List<Medico>> listarActivos() {

        return ResponseEntity.ok(
                medicoService.listarActivos()
        );
    }

    /**
     * Actualizar médico
     */
    @PutMapping("/{id}")
    public ResponseEntity<Medico> actualizarMedico(
            @PathVariable Long id,
            @Valid @RequestBody MedicoRequest request) {

        return ResponseEntity.ok(
                medicoService.actualizarMedico(id, request)
        );
    }

    /**
     * Activar médico
     */
    @PutMapping("/{id}/activar")
    public ResponseEntity<Medico> activarMedico(
            @PathVariable Long id) {

        return ResponseEntity.ok(
                medicoService.activarMedico(id)
        );
    }

    /**
     * Desactivar médico
     */
    @PutMapping("/{id}/desactivar")
    public ResponseEntity<Medico> desactivarMedico(
            @PathVariable Long id) {

        return ResponseEntity.ok(
                medicoService.desactivarMedico(id)
        );
    }

    /**
     * Eliminar médico
     */
    @DeleteMapping("/{id}")
    public ResponseEntity<Void> eliminarMedico(
            @PathVariable Long id) {

        medicoService.eliminarMedico(id);

        return ResponseEntity.noContent().build();
    }

    /**
     * Contar médicos
     */
    @GetMapping("/count")
    public ResponseEntity<Long> contarMedicos() {

        return ResponseEntity.ok(
                medicoService.contarMedicos()
        );
    }

    /**
     * Verificar si existe un médico
     */
    @GetMapping("/exists/{id}")
    public ResponseEntity<Boolean> existeMedico(
            @PathVariable Long id) {

        return ResponseEntity.ok(
                medicoService.existeMedico(id)
        );
    }

}