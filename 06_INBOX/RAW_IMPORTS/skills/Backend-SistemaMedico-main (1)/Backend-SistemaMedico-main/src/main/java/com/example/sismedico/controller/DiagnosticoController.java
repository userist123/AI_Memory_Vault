package com.example.sismedico.controller;

import com.example.sismedico.dto.request.DiagnosticoRequest;
import com.example.sismedico.entity.Diagnostico;
import com.example.sismedico.service.DiagnosticoService;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

import java.util.List;

@RestController
@RequestMapping("/api/diagnosticos")
@RequiredArgsConstructor
@CrossOrigin(origins = "*")
public class DiagnosticoController {

    private final DiagnosticoService diagnosticoService;

    /**
     * Registrar diagnóstico
     */
    @PostMapping
    public ResponseEntity<Diagnostico> registrarDiagnostico(
            @Valid @RequestBody DiagnosticoRequest request) {

        return ResponseEntity.status(HttpStatus.CREATED)
                .body(diagnosticoService.registrarDiagnostico(request));
    }

    /**
     * Listar diagnósticos
     */
    @GetMapping
    public ResponseEntity<List<Diagnostico>> listarDiagnosticos() {

        return ResponseEntity.ok(
                diagnosticoService.listarDiagnosticos()
        );
    }

    /**
     * Obtener diagnóstico por ID
     */
    @GetMapping("/{id}")
    public ResponseEntity<Diagnostico> obtenerPorId(
            @PathVariable Long id) {

        return ResponseEntity.ok(
                diagnosticoService.obtenerPorId(id)
        );
    }

    /**
     * Buscar diagnóstico por cita
     */
    @GetMapping("/cita/{citaId}")
    public ResponseEntity<Diagnostico> buscarPorCita(
            @PathVariable Long citaId) {

        return ResponseEntity.ok(
                diagnosticoService.buscarPorCita(citaId)
        );
    }

    /**
     * Actualizar diagnóstico
     */
    @PutMapping("/{id}")
    public ResponseEntity<Diagnostico> actualizarDiagnostico(
            @PathVariable Long id,
            @Valid @RequestBody DiagnosticoRequest request) {

        return ResponseEntity.ok(
                diagnosticoService.actualizarDiagnostico(id, request)
        );
    }

    /**
     * Dar alta médica
     */
    @PutMapping("/{id}/alta")
    public ResponseEntity<Diagnostico> darAltaMedica(
            @PathVariable Long id) {

        return ResponseEntity.ok(
                diagnosticoService.darAltaMedica(id)
        );
    }

    /**
     * Quitar alta médica
     */
    @PutMapping("/{id}/quitar-alta")
    public ResponseEntity<Diagnostico> quitarAltaMedica(
            @PathVariable Long id) {

        return ResponseEntity.ok(
                diagnosticoService.quitarAltaMedica(id)
        );
    }

    /**
     * Eliminar diagnóstico
     */
    @DeleteMapping("/{id}")
    public ResponseEntity<Void> eliminarDiagnostico(
            @PathVariable Long id) {

        diagnosticoService.eliminarDiagnostico(id);

        return ResponseEntity.noContent().build();
    }

    /**
     * Contar diagnósticos
     */
    @GetMapping("/count")
    public ResponseEntity<Long> contarDiagnosticos() {

        return ResponseEntity.ok(
                diagnosticoService.contarDiagnosticos()
        );
    }

    /**
     * Verificar si existe un diagnóstico
     */
    @GetMapping("/exists/{id}")
    public ResponseEntity<Boolean> existeDiagnostico(
            @PathVariable Long id) {

        return ResponseEntity.ok(
                diagnosticoService.existeDiagnostico(id)
        );
    }

}