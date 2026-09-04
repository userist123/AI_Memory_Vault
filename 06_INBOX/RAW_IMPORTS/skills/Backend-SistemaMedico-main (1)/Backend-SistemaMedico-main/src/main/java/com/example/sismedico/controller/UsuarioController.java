package com.example.sismedico.controller;

import com.example.sismedico.dto.request.UsuarioRequest;
import com.example.sismedico.entity.Usuario;
import com.example.sismedico.service.UsuarioService;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

import java.util.List;

@RestController
@RequestMapping("/api/usuarios")
@RequiredArgsConstructor
@CrossOrigin(origins = "*")
public class UsuarioController {

    private final UsuarioService usuarioService;

    /**
     * Registrar usuario
     */
    @PostMapping
    public ResponseEntity<Usuario> registrarUsuario(
            @Valid @RequestBody UsuarioRequest request) {

        return ResponseEntity.status(HttpStatus.CREATED)
                .body(usuarioService.registrarUsuario(request));
    }

    /**
     * Listar usuarios
     */
    @GetMapping
    public ResponseEntity<List<Usuario>> listarUsuarios() {

        return ResponseEntity.ok(
                usuarioService.listarUsuarios()
        );
    }

    /**
     * Obtener usuario por ID
     */
    @GetMapping("/{id}")
    public ResponseEntity<Usuario> obtenerPorId(
            @PathVariable Long id) {

        return ResponseEntity.ok(
                usuarioService.obtenerPorId(id)
        );
    }

    /**
     * Buscar usuario por correo
     */
    @GetMapping("/correo/{correo}")
    public ResponseEntity<Usuario> buscarPorCorreo(
            @PathVariable String correo) {

        return ResponseEntity.ok(
                usuarioService.buscarPorCorreo(correo)
        );
    }

    /**
     * Listar usuarios activos
     */
    @GetMapping("/activos")
    public ResponseEntity<List<Usuario>> listarActivos() {

        return ResponseEntity.ok(
                usuarioService.listarActivos()
        );
    }

    /**
     * Actualizar usuario
     */
    @PutMapping("/{id}")
    public ResponseEntity<Usuario> actualizarUsuario(
            @PathVariable Long id,
            @Valid @RequestBody UsuarioRequest request) {

        return ResponseEntity.ok(
                usuarioService.actualizarUsuario(id, request)
        );
    }

    /**
     * Activar usuario
     */
    @PutMapping("/{id}/activar")
    public ResponseEntity<Usuario> activarUsuario(
            @PathVariable Long id) {

        return ResponseEntity.ok(
                usuarioService.activarUsuario(id)
        );
    }

    /**
     * Desactivar usuario
     */
    @PutMapping("/{id}/desactivar")
    public ResponseEntity<Usuario> desactivarUsuario(
            @PathVariable Long id) {

        return ResponseEntity.ok(
                usuarioService.desactivarUsuario(id)
        );
    }

    /**
     * Verificar correo electrónico
     */
    @PutMapping("/{id}/verificar-email")
    public ResponseEntity<Usuario> verificarEmail(
            @PathVariable Long id) {

        return ResponseEntity.ok(
                usuarioService.verificarEmail(id)
        );
    }

    /**
     * Actualizar token de Firebase
     */
    @PutMapping("/{id}/firebase")
    public ResponseEntity<Usuario> actualizarTokenFirebase(
            @PathVariable Long id,
            @RequestParam String token) {

        return ResponseEntity.ok(
                usuarioService.actualizarTokenFirebase(id, token)
        );
    }

    /**
     * Eliminar usuario
     */
    @DeleteMapping("/{id}")
    public ResponseEntity<Void> eliminarUsuario(
            @PathVariable Long id) {

        usuarioService.eliminarUsuario(id);

        return ResponseEntity.noContent().build();
    }

    /**
     * Contar usuarios
     */
    @GetMapping("/count")
    public ResponseEntity<Long> contarUsuarios() {

        return ResponseEntity.ok(
                usuarioService.contarUsuarios()
        );
    }

    /**
     * Verificar si existe un usuario
     */
    @GetMapping("/exists/{id}")
    public ResponseEntity<Boolean> existeUsuario(
            @PathVariable Long id) {

        return ResponseEntity.ok(
                usuarioService.existeUsuario(id)
        );
    }

}