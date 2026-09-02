package com.example.sismedico.controller;

import com.example.sismedico.dto.request.HorarioRequest;
import com.example.sismedico.entity.Horario;
import com.example.sismedico.service.HorarioService;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

import java.util.List;

@RestController
@RequestMapping("/api/horarios")
@RequiredArgsConstructor
@CrossOrigin(origins = "*")
public class HorarioController {

    private final HorarioService horarioService;

    /**
     * Registrar horario
     */
    @PostMapping
    public ResponseEntity<Horario> registrarHorario(
            @Valid @RequestBody HorarioRequest request) {

        return ResponseEntity.status(HttpStatus.CREATED)
                .body(horarioService.registrarHorario(request));
    }

    /**
     * Listar horarios
     */
    @GetMapping
    public ResponseEntity<List<Horario>> listarHorarios() {

        return ResponseEntity.ok(
                horarioService.listarHorarios()
        );
    }

    /**
     * Obtener horario por ID
     */
    @GetMapping("/{id}")
    public ResponseEntity<Horario> obtenerPorId(
            @PathVariable Long id) {

        return ResponseEntity.ok(
                horarioService.obtenerPorId(id)
        );
    }

    /**
     * Obtener horarios por médico
     */
    @GetMapping("/medico/{medicoId}")
    public ResponseEntity<List<Horario>> listarPorMedico(
            @PathVariable Long medicoId) {

        return ResponseEntity.ok(
                horarioService.listarPorMedico(medicoId)
        );
    }

    /**
     * Actualizar horario
     */
    @PutMapping("/{id}")
    public ResponseEntity<Horario> actualizarHorario(
            @PathVariable Long id,
            @Valid @RequestBody HorarioRequest request) {

        return ResponseEntity.ok(
                horarioService.actualizarHorario(id, request)
        );
    }

    /**
     * Activar horario
     */
    @PutMapping("/{id}/activar")
    public ResponseEntity<Horario> activarHorario(
            @PathVariable Long id) {

        return ResponseEntity.ok(
                horarioService.activarHorario(id)
        );
    }

    /**
     * Desactivar horario
     */
    @PutMapping("/{id}/desactivar")
    public ResponseEntity<Horario> desactivarHorario(
            @PathVariable Long id) {

        return ResponseEntity.ok(
                horarioService.desactivarHorario(id)
        );
    }

    /**
     * Eliminar horario
     */
    @DeleteMapping("/{id}")
    public ResponseEntity<Void> eliminarHorario(
            @PathVariable Long id) {

        horarioService.eliminarHorario(id);

        return ResponseEntity.noContent().build();
    }

    /**
     * Contar horarios
     */
    @GetMapping("/count")
    public ResponseEntity<Long> contarHorarios() {

        return ResponseEntity.ok(
                horarioService.contarHorarios()
        );
    }

    /**
     * Verificar si existe un horario
     */
    @GetMapping("/exists/{id}")
    public ResponseEntity<Boolean> existeHorario(
            @PathVariable Long id) {

        return ResponseEntity.ok(
                horarioService.existeHorario(id)
        );
    }

}