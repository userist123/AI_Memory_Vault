package com.example.sismedico.controller;

import com.example.sismedico.dto.request.PacienteRequest;
import com.example.sismedico.entity.Paciente;
import com.example.sismedico.service.PacienteService;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

import java.util.List;

@RestController
@RequestMapping("/api/pacientes")
@RequiredArgsConstructor
@CrossOrigin(origins = "*")
public class PacienteController {

    private final PacienteService pacienteService;

    /**
     * Registrar paciente
     */
    @PostMapping
    public ResponseEntity<Paciente> registrarPaciente(
            @Valid @RequestBody PacienteRequest request) {

        return ResponseEntity.status(HttpStatus.CREATED)
                .body(pacienteService.registrarPaciente(request));
    }

    /**
     * Listar pacientes
     */
    @GetMapping
    public ResponseEntity<List<Paciente>> listarPacientes() {

        return ResponseEntity.ok(
                pacienteService.listarPacientes()
        );
    }

    /**
     * Obtener paciente por ID
     */
    @GetMapping("/{id}")
    public ResponseEntity<Paciente> obtenerPorId(
            @PathVariable Long id) {

        return ResponseEntity.ok(
                pacienteService.obtenerPorId(id)
        );
    }

    /**
     * Listar pacientes activos
     */
    @GetMapping("/activos")
    public ResponseEntity<List<Paciente>> listarActivos() {

        return ResponseEntity.ok(
                pacienteService.listarActivos()
        );
    }

    /**
     * Actualizar paciente
     */
    @PutMapping("/{id}")
    public ResponseEntity<Paciente> actualizarPaciente(
            @PathVariable Long id,
            @Valid @RequestBody PacienteRequest request) {

        return ResponseEntity.ok(
                pacienteService.actualizarPaciente(id, request)
        );
    }

    /**
     * Activar paciente
     */
    @PutMapping("/{id}/activar")
    public ResponseEntity<Paciente> activarPaciente(
            @PathVariable Long id) {

        return ResponseEntity.ok(
                pacienteService.activarPaciente(id)
        );
    }

    /**
     * Desactivar paciente
     */
    @PutMapping("/{id}/desactivar")
    public ResponseEntity<Paciente> desactivarPaciente(
            @PathVariable Long id) {

        return ResponseEntity.ok(
                pacienteService.desactivarPaciente(id)
        );
    }

    /**
     * Eliminar paciente
     */
    @DeleteMapping("/{id}")
    public ResponseEntity<Void> eliminarPaciente(
            @PathVariable Long id) {

        pacienteService.eliminarPaciente(id);

        return ResponseEntity.noContent().build();
    }

    /**
     * Contar pacientes
     */
    @GetMapping("/count")
    public ResponseEntity<Long> contarPacientes() {

        return ResponseEntity.ok(
                pacienteService.contarPacientes()
        );
    }

    /**
     * Verificar si existe un paciente
     */
    @GetMapping("/exists/{id}")
    public ResponseEntity<Boolean> existePaciente(
            @PathVariable Long id) {

        return ResponseEntity.ok(
                pacienteService.existePaciente(id)
        );
    }

}