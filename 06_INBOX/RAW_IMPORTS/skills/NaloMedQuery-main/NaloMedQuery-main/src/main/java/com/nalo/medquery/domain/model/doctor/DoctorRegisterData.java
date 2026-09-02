package com.nalo.medquery.domain.model.doctor;

import com.nalo.medquery.domain.model.AddressData;

import jakarta.validation.Valid;
import jakarta.validation.constraints.Email;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotNull;
import jakarta.validation.constraints.Pattern;

public record DoctorRegisterData(
    @NotBlank
    String nome,
    @NotBlank
    @Email
    String email,
    @NotBlank
    String telefone,
    @NotBlank
    @Pattern(regexp = "\\d{4,6}") 
    String crm, // digitos de 4 á 6 (regex)
    @NotNull
    EnumDoctorSpecialty especialidade,
    @NotNull
    @Valid
    AddressData endereco
) {}
