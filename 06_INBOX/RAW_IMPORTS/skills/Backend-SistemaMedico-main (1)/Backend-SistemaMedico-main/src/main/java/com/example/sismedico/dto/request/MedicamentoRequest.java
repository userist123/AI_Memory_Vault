package com.example.sismedico.dto.request;

import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Size;
import lombok.*;

@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class MedicamentoRequest {

    @NotBlank(message = "El nombre del medicamento es obligatorio")
    @Size(max = 150)
    private String nombre;

    @NotBlank(message = "La dosis es obligatoria")
    @Size(max = 100)
    private String dosis;

    @NotBlank(message = "La frecuencia es obligatoria")
    @Size(max = 100)
    private String frecuencia;

    @NotBlank(message = "La duración es obligatoria")
    @Size(max = 100)
    private String duracion;

    @Size(max = 500)
    private String instrucciones;

}