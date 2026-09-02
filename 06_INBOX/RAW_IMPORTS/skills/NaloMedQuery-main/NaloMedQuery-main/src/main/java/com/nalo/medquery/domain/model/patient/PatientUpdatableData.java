package com.nalo.medquery.domain.model.patient;

import com.nalo.medquery.domain.model.AddressData;

import jakarta.validation.Valid;
import jakarta.validation.constraints.NotNull;

public record PatientUpdatableData(
    @NotNull
    Long id,
    String nome,
    String telefone,
    @Valid
    AddressData endereco
) {}
