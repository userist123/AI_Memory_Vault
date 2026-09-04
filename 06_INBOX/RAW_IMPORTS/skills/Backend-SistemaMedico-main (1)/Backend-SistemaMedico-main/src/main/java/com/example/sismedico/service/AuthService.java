package com.example.sismedico.service;

import com.example.sismedico.dto.request.LoginRequest;
import com.example.sismedico.dto.request.RegisterRequest;
import com.example.sismedico.entity.Rol;
import com.example.sismedico.entity.Usuario;
import com.example.sismedico.repository.RolRepository;
import com.example.sismedico.repository.UsuarioRepository;
import com.example.sismedico.security.jwt.JwtService;
import lombok.RequiredArgsConstructor;
import org.springframework.security.authentication.AuthenticationManager;
import org.springframework.security.authentication.UsernamePasswordAuthenticationToken;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.stereotype.Service;

@Service
@RequiredArgsConstructor
public class AuthService {

    private final UsuarioRepository usuarioRepository;
    private final RolRepository rolRepository;
    private final PasswordEncoder passwordEncoder;
    private final JwtService jwtService;
    private final AuthenticationManager authenticationManager;

    /**
     * Registro de usuarios
     */
    public String register(RegisterRequest request) {

        if (usuarioRepository.existsByCorreo(request.getCorreo())) {
            throw new RuntimeException("El correo ya está registrado.");
        }

        if (!request.getPassword().equals(request.getConfirmarPassword())) {
            throw new RuntimeException("Las contraseñas no coinciden.");
        }

        Rol rol = rolRepository.findById(request.getRolId())
                .orElseThrow(() -> new RuntimeException("Rol no encontrado."));

        Usuario usuario = Usuario.builder()
                .nombre(request.getNombre())
                .apellido(request.getApellido())
                .correo(request.getCorreo())
                .password(passwordEncoder.encode(request.getPassword()))
                .telefono(request.getTelefono())
                .direccion(request.getDireccion())
                .rol(rol)
                .activo(true)
                .emailVerificado(false)
                .build();

        usuarioRepository.save(usuario);

        return "Usuario registrado correctamente.";
    }

    /**
     * Inicio de sesión
     */
    public String login(LoginRequest request) {

        authenticationManager.authenticate(
                new UsernamePasswordAuthenticationToken(
                        request.getCorreo(),
                        request.getPassword()
                )
        );

        Usuario usuario = usuarioRepository.findByCorreo(request.getCorreo())
                .orElseThrow(() -> new RuntimeException("Usuario no encontrado."));

        usuario.setUltimoAcceso(java.time.LocalDateTime.now());
        usuarioRepository.save(usuario);

        return jwtService.generateToken(
                org.springframework.security.core.userdetails.User
                        .withUsername(usuario.getCorreo())
                        .password(usuario.getPassword())
                        .authorities(
                                "ROLE_" + usuario.getRol().getNombre().name()
                        )
                        .build()
        );
    }

}