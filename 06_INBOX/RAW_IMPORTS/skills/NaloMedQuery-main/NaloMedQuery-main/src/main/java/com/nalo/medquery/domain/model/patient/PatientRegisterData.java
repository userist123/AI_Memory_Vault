package com.nalo.medquery.domain.model.patient;

import com.nalo.medquery.domain.model.AddressData;

import jakarta.validation.Valid;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotNull;

public record PatientRegisterData(
    @NotBlank
    String nome,
    @NotBlank
    String email,
    @NotBlank
    String telefone,
    @NotBlank
    String cpf,
    @NotNull
    @Valid
    AddressData endereco
) {}