package com.nalo.medquery.domain.model.doctor;

import com.nalo.medquery.domain.model.AddressData;

import jakarta.validation.Valid;
import jakarta.validation.constraints.NotNull;

//used in the DoctorEnity when updating things
public record DoctorUpdatableData(    
    @NotNull
    Long id,
    String nome,
    String telefone,
    @Valid
    AddressData endereco
) {}
