package com.example.sismedico.dto.request;

import jakarta.validation.Valid;
import jakarta.validation.constraints.NotEmpty;
import jakarta.validation.constraints.NotNull;
import jakarta.validation.constraints.Size;
import lombok.*;

import java.util.List;

@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class RecetaRequest {

    @NotNull(message = "El diagnóstico es obligatorio")
    private Long diagnosticoId;

    @Size(max = 1000, message = "Las indicaciones generales no pueden exceder los 1000 caracteres")
    private String indicacionesGenerales;

    @Valid
    @NotEmpty(message = "Debe agregar al menos un medicamento")
    private List<MedicamentoRequest> medicamentos;

}