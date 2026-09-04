package com.example.sismedico.entity;

import jakarta.persistence.*;
import lombok.*;

import java.time.LocalDate;
import java.time.LocalDateTime;
import java.util.ArrayList;
import java.util.List;

@Entity
@Table(name = "pacientes")
@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class Paciente {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    // Usuario asociado al paciente
    @OneToOne(fetch = FetchType.LAZY)
    @JoinColumn(name = "usuario_id", nullable = false, unique = true)
    private Usuario usuario;

    @Column(nullable = false, unique = true, length = 18)
    private String curp;

    @Column(unique = true, length = 20)
    private String numeroSeguroSocial;

    @Column(nullable = false)
    private LocalDate fechaNacimiento;

    @Column(length = 5)
    private String tipoSangre;

    @Column(length = 500)
    private String alergias;

    @Column(length = 500)
    private String enfermedadesCronicas;

    @Column(length = 500)
    private String medicamentosActuales;

    @Column(length = 100)
    private String contactoEmergencia;

    @Column(length = 20)
    private String telefonoEmergencia;

    @Column(nullable = false)
    private Double peso;

    @Column(nullable = false)
    private Double altura;

    @Builder.Default
    @OneToMany(
            mappedBy = "paciente",
            cascade = CascadeType.ALL,
            orphanRemoval = true,
            fetch = FetchType.LAZY
    )
    private List<Cita> citas = new ArrayList<>();

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