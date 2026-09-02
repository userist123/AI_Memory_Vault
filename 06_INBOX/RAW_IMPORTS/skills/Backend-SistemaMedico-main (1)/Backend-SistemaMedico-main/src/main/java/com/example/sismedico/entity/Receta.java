package com.example.sismedico.entity;

import jakarta.persistence.*;
import lombok.*;

import java.time.LocalDateTime;
import java.util.ArrayList;
import java.util.List;

@Entity
@Table(name = "recetas")
@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class Receta {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @OneToOne(fetch = FetchType.LAZY)
    @JoinColumn(name = "diagnostico_id", nullable = false, unique = true)
    private Diagnostico diagnostico;

    @Builder.Default
    @OneToMany(
            mappedBy = "receta",
            cascade = CascadeType.ALL,
            orphanRemoval = true
    )
    private List<MedicamentoReceta> medicamentos = new ArrayList<>();

    @Column(length = 1000)
    private String indicacionesGenerales;

    @Column(nullable = false, updatable = false)
    private LocalDateTime fechaRegistro;

    @PrePersist
    public void prePersist() {
        fechaRegistro = LocalDateTime.now();
    }

}