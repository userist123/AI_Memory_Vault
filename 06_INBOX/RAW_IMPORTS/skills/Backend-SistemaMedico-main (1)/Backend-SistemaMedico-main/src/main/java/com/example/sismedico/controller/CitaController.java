package com.example.sismedico.controller;

import com.example.sismedico.dto.request.CancelarCitaRequest;
import com.example.sismedico.dto.request.CitaRequest;
import com.example.sismedico.dto.request.ReprogramarCitaRequest;
import com.example.sismedico.dto.response.CitaResponse;
import com.example.sismedico.service.CitaService;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

import java.util.List;

@RestController
@RequestMapping("/api/citas")
@RequiredArgsConstructor
@CrossOrigin(origins = "*")
public class CitaController {

    private final CitaService citaService;

    /**
     * Registrar una nueva cita
     */
    @PostMapping
    public ResponseEntity<CitaResponse> crearCita(
            @Valid @RequestBody CitaRequest request) {

        return ResponseEntity
                .status(HttpStatus.CREATED)
                .body(citaService.crearCita(request));
    }

    /**
     * Obtener todas las citas
     */
    @GetMapping
    public ResponseEntity<List<CitaResponse>> listarCitas() {

        return ResponseEntity.ok(citaService.listarCitas());

    }

    /**
     * Buscar una cita por ID
     */
    @GetMapping("/{id}")
    public ResponseEntity<CitaResponse> obtenerCita(
            @PathVariable Long id) {

        return ResponseEntity.ok(citaService.obtenerPorId(id));

    }

    /**
     * Actualizar una cita
     */
    @PutMapping("/{id}")
    public ResponseEntity<CitaResponse> actualizarCita(
            @PathVariable Long id,
            @Valid @RequestBody CitaRequest request) {

        return ResponseEntity.ok(
                citaService.actualizarCita(id, request)
        );

    }

    /**
     * Cancelar una cita
     */
    @PutMapping("/{id}/cancelar")
    public ResponseEntity<String> cancelarCita(
            @PathVariable Long id,
            @Valid @RequestBody CancelarCitaRequest request) {

        citaService.cancelarCita(id, request);

        return ResponseEntity.ok("La cita fue cancelada correctamente.");

    }

    /**
     * Reprogramar una cita
     */
    @PutMapping("/{id}/reprogramar")
    public ResponseEntity<CitaResponse> reprogramarCita(
            @PathVariable Long id,
            @Valid @RequestBody ReprogramarCitaRequest request) {

        return ResponseEntity.ok(
                citaService.reprogramarCita(id, request)
        );

    }

    /**
     * Eliminar una cita
     */
    @DeleteMapping("/{id}")
    public ResponseEntity<Void> eliminarCita(
            @PathVariable Long id) {

        citaService.eliminarCita(id);

        return ResponseEntity.noContent().build();

    }

}