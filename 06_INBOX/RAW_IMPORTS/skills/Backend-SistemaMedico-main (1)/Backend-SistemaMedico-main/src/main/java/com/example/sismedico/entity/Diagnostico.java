package com.example.sismedico.entity;

import jakarta.persistence.*;
import lombok.*;

import java.time.LocalDate;
import java.time.LocalDateTime;

@Entity
@Table(name = "diagnosticos")
@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class Diagnostico {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    // Una cita tiene un diagnóstico
    @OneToOne(fetch = FetchType.LAZY)
    @JoinColumn(name = "cita_id", nullable = false, unique = true)
    private Cita cita;

    @Column(nullable = false, length = 1000)
    private String diagnostico;

    @Column(length = 2000)
    private String tratamiento;

    @Column(length = 2000)
    private String observaciones;

    // ==========================
    // Signos vitales
    // ==========================

    private Double temperatura;

    private Integer frecuenciaCardiaca;

    private Integer frecuenciaRespiratoria;

    @Column(length = 20)
    private String presionArterial;

    private Double peso;

    private Double altura;

    @Column(length = 1000)
    private String recomendaciones;

    private LocalDate proximaConsulta;

    @Column(nullable = false)
    @Builder.Default
    private Boolean altaMedica = false;

    // Relación con la receta médica
    @OneToOne(
            mappedBy = "diagnostico",
            cascade = CascadeType.ALL,
            orphanRemoval = true,
            fetch = FetchType.LAZY
    )
    private Receta receta;

    @Column(nullable = false, updatable = false)
    private LocalDateTime fechaRegistro;

    @PrePersist
    public void prePersist() {

        fechaRegistro = LocalDateTime.now();

        if (altaMedica == null) {
            altaMedica = false;
        }

    }

}