package com.example.sismedico.entity;

import com.example.sismedico.enums.TipoNotificacion;
import jakarta.persistence.*;
import lombok.*;

import java.time.LocalDateTime;

@Entity
@Table(name = "notificaciones")
@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class Notificacion {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    // Usuario que recibirá la notificación
    @ManyToOne(fetch = FetchType.LAZY)
    @JoinColumn(name = "usuario_id", nullable = false)
    private Usuario usuario;

    // Cita relacionada (opcional)
    @ManyToOne(fetch = FetchType.LAZY)
    @JoinColumn(name = "cita_id")
    private Cita cita;

    @Enumerated(EnumType.STRING)
    @Column(nullable = false)
    private TipoNotificacion tipo;

    @Column(nullable = false, length = 150)
    private String titulo;

    @Column(nullable = false, length = 1000)
    private String mensaje;

    @Column(nullable = false)
    @Builder.Default
    private Boolean leida = false;

    @Column(nullable = false)
    @Builder.Default
    private Boolean enviada = false;

    @Column(nullable = false, updatable = false)
    private LocalDateTime fechaRegistro;

    private LocalDateTime fechaLectura;

    @PrePersist
    public void prePersist() {

        fechaRegistro = LocalDateTime.now();

        if (leida == null) {
            leida = false;
        }

        if (enviada == null) {
            enviada = false;
        }

    }

}