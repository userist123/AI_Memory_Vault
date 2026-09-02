package com.example.sismedico.entity;

import com.example.sismedico.enums.DiaSemana;
import jakarta.persistence.*;
import lombok.*;

import java.time.LocalDateTime;
import java.time.LocalTime;

@Entity
@Table(name = "horarios")
@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class Horario {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;


    @ManyToOne(fetch = FetchType.LAZY)
    @JoinColumn(name = "medico_id", nullable = false)
    private Medico medico;


    @Enumerated(EnumType.STRING)
    @Column(nullable = false)
    private DiaSemana diaSemana;


    @Column(nullable = false)
    private LocalTime horaInicio;


    @Column(nullable = false)
    private LocalTime horaFin;


    @Column(nullable = false)
    @Builder.Default
    private Boolean activo = true;


    @Column(nullable = false, updatable = false)
    private LocalDateTime fechaRegistro;


    @PrePersist
    public void prePersist() {

        fechaRegistro = LocalDateTime.now();

        if (activo == null) {
            activo = true;
        }

    }

}