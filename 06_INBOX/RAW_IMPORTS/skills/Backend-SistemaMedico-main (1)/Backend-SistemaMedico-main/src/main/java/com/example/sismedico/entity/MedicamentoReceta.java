package com.example.sismedico.entity;

import jakarta.persistence.*;
import lombok.*;

@Entity
@Table(name = "medicamentos_receta")
@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class MedicamentoReceta {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @ManyToOne(fetch = FetchType.LAZY)
    @JoinColumn(name = "receta_id", nullable = false)
    private Receta receta;

    @Column(nullable = false)
    private String nombre;

    @Column(nullable = false)
    private String dosis;

    @Column(nullable = false)
    private String frecuencia;

    @Column(nullable = false)
    private String duracion;

    @Column(length = 500)
    private String instrucciones;

}