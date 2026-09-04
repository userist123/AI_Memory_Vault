package com.example.sismedico.dto.request;

import jakarta.validation.constraints.*;
import lombok.*;

import java.time.LocalDate;

@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class DiagnosticoRequest {

    @NotNull(message = "La cita es obligatoria")
    private Long citaId;

    @NotBlank(message = "El diagnóstico es obligatorio")
    @Size(max = 1000, message = "El diagnóstico no puede exceder los 1000 caracteres")
    private String diagnostico;

    @Size(max = 2000, message = "El tratamiento no puede exceder los 2000 caracteres")
    private String tratamiento;

    @Size(max = 2000, message = "Las observaciones no pueden exceder los 2000 caracteres")
    private String observaciones;

    @DecimalMin(value = "30.0", message = "Temperatura inválida")
    @DecimalMax(value = "45.0", message = "Temperatura inválida")
    private Double temperatura;

    @Min(value = 20, message = "Frecuencia cardíaca inválida")
    @Max(value = 250, message = "Frecuencia cardíaca inválida")
    private Integer frecuenciaCardiaca;

    @Min(value = 5, message = "Frecuencia respiratoria inválida")
    @Max(value = 80, message = "Frecuencia respiratoria inválida")
    private Integer frecuenciaRespiratoria;

    @Pattern(
            regexp = "^\\d{2,3}/\\d{2,3}$",
            message = "La presión arterial debe tener el formato 120/80"
    )
    private String presionArterial;

    @DecimalMin(value = "0.5", message = "Peso inválido")
    @DecimalMax(value = "500.0", message = "Peso inválido")
    private Double peso;

    @DecimalMin(value = "0.30", message = "Altura inválida")
    @DecimalMax(value = "3.00", message = "Altura inválida")
    private Double altura;

    @Size(max = 1000, message = "Las recomendaciones no pueden exceder los 1000 caracteres")
    private String recomendaciones;

    private LocalDate proximaConsulta;

    @Builder.Default
    private Boolean altaMedica = false;

}
